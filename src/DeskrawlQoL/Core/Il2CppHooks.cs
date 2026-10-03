using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;

namespace DeskrawlQoL.Core;

/// <summary>
/// Hooking without touching game code.
///
/// On Unity 6000.3 / IL2CPP metadata v39, Il2CppInterop's Harmony support and class injection
/// (https://github.com/BepInEx/Il2CppInterop/issues/283) and Dobby inline detours all crash the game.
/// Instead we swap function pointers in IL2CPP's own data structures:
///  - virtual methods: the class vtable entry (VirtualInvokeData { methodPtr, MethodInfo* })
///  - Unity messages (Update/OnGUI/Awake): MethodInfo.methodPointer, which Unity invokes through.
///
/// Native signatures for hook delegates: IL2CPP x64 instance methods are
/// <c>ret fn(this, args..., MethodInfo*)</c>; bools are one byte, enums their underlying type.
///
/// Game method names are obfuscated and change between builds, so look methods up by signature
/// with <see cref="FindBySignature"/> rather than by name wherever possible.
/// </summary>
internal static unsafe class Il2CppHooks
{
    // Hook delegates must stay alive for the lifetime of the process.
    private static readonly List<object> KeepAlive = new();

    public static MethodInfo Method(Type t, string name) =>
        t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);

    /// <summary>Finds a method declared on <paramref name="type"/> by return and parameter types.</summary>
    public static MethodInfo FindBySignature(Type type, Type ret, params Type[] args)
    {
        var matches = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == ret && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args))
            .ToList();
        if (matches.Count > 1) Plugin.L.LogWarning($"{matches.Count} candidates on {type.Name}; using {matches[0].Name}.");
        return matches.FirstOrDefault();
    }

    private static IntPtr MethodInfoPtr(MethodInfo m) =>
        (IntPtr)Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m).GetValue(null);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);

    private static void WritePtr(IntPtr* slot, IntPtr value)
    {
        VirtualProtect((IntPtr)slot, (UIntPtr)8, 0x04 /* PAGE_READWRITE */, out uint old);
        *slot = value;
        VirtualProtect((IntPtr)slot, (UIntPtr)8, old, out _);
    }

    /// <summary>
    /// Redirects a non-virtual method that Unity invokes through its MethodInfo (Update, OnGUI, Awake...).
    /// Direct calls from other game code are NOT intercepted. Returns the original function.
    /// </summary>
    public static T HookMethodInfo<T>(MethodInfo m, T hook) where T : Delegate
    {
        if (m == null) { Plugin.L.LogWarning($"Method for {hook.Method.Name} not found."); return null; }
        var mi = (IntPtr*)MethodInfoPtr(m);
        IntPtr orig = mi[0];
        IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(hook);
        KeepAlive.Add(hook);
        WritePtr(&mi[0], hookPtr);
        if (mi[1] == orig) WritePtr(&mi[1], hookPtr); // virtualMethodPointer
        Plugin.L.LogDebug($"Hooked {m.DeclaringType?.Name}.{m.Name}.");
        return Marshal.GetDelegateForFunctionPointer<T>(orig);
    }

    /// <summary>
    /// Redirects a virtual method's vtable slot in its declaring class and every subclass.
    /// Calls through the vtable are intercepted; direct (non-virtual) calls such as base.X() are not.
    /// Returns the original function immediately; slots are patched as the game initializes each class.
    /// </summary>
    public static T HookVirtual<T>(MethodInfo m, T hook) where T : Delegate
    {
        var baseType = m.DeclaringType;
        IntPtr mi = MethodInfoPtr(m);
        IntPtr orig = *(IntPtr*)mi;
        IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(hook);
        KeepAlive.Add(hook);

        var pending = new List<(string name, IntPtr klass)>();
        foreach (var t in baseType.Assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t)))
        {
            try
            {
                var store = typeof(Il2CppClassPointerStore<>).MakeGenericType(t);
                var klass = (IntPtr)store.GetField("NativeClassPtr").GetValue(null);
                if (klass != IntPtr.Zero) pending.Add((t.Name, klass));
            }
            catch { /* not an il2cpp type */ }
        }

        // IL2CPP on Unity 6000.3 fills vtables lazily (when the game first uses the class), so a
        // background thread waits for each class's { orig, mi } pair to appear and swaps the
        // function pointer. A single aligned pointer write is safe to race with readers.
        var thread = new Thread(() =>
        {
            while (pending.Count > 0)
            {
                for (int k = pending.Count - 1; k >= 0; k--)
                {
                    var (name, klass) = pending[k];
                    var p = (IntPtr*)klass;
                    for (int i = 0; i < 0x1000 / 8 - 1; i++)
                    {
                        if (p[i] != orig || p[i + 1] != mi) continue;
                        WritePtr(&p[i], hookPtr);
                        Plugin.L.LogDebug($"Hooked {name}.{m.Name} (vtable +0x{i * 8:X}).");
                        pending.RemoveAt(k);
                        break;
                    }
                }
                Thread.Sleep(500);
            }
        }) { IsBackground = true, Name = $"DeskrawlQoL vtable patcher ({baseType.Name}.{m.Name})" };
        thread.Start();
        KeepAlive.Add(thread);
        return Marshal.GetDelegateForFunctionPointer<T>(orig);
    }

    public static IntPtr ClassOf<T>() => Il2CppClassPointerStore<T>.NativeClassPtr;

    public static bool IsA(IntPtr obj, IntPtr klass) =>
        obj != IntPtr.Zero && klass != IntPtr.Zero && IL2CPP.il2cpp_class_is_subclass_of(IL2CPP.il2cpp_object_get_class(obj), klass, false);

    public static string ClassName(IntPtr obj) =>
        obj == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(IL2CPP.il2cpp_object_get_class(obj))) ?? "";

    /// <summary>Byte offset of an instance field, or <paramref name="fallback"/> if not found.</summary>
    public static int FieldOffset<T>(string fieldName, int fallback)
    {
        var field = IL2CPP.il2cpp_class_get_field_from_name(ClassOf<T>(), fieldName);
        return field != IntPtr.Zero ? (int)IL2CPP.il2cpp_field_get_offset(field) : fallback;
    }

    /// <summary>UnityEngine.Object name without "(Clone)".</summary>
    public static string ObjName(IntPtr obj)
    {
        string n = new UnityEngine.Object(obj).name;
        return string.IsNullOrEmpty(n) ? "?" : n.Replace("(Clone)", "").Trim();
    }
}
