using System;
using System.Runtime.InteropServices;

namespace DeskrawlQoL.Core;

/// <summary>
/// Looks at the game's native code (GameAssembly.dll): which function an address belongs to, and what it calls.
/// Functions are found through the DLL's unwind table (.pdata), so any address inside a function maps to it.
/// </summary>
internal static unsafe class GameCode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeFunction { public uint Begin, End, UnwindData; }

    [DllImport("kernel32.dll")]
    private static extern RuntimeFunction* RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr historyTable);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    private static ulong _base;
    private static ulong Base => _base != 0 ? _base : _base = (ulong)GetModuleHandleW("GameAssembly.dll");

    /// <summary>Start and end address of the game function containing <paramref name="address"/>, or (0, 0).</summary>
    public static (ulong start, ulong end) Function(IntPtr address)
    {
        var rf = RtlLookupFunctionEntry((ulong)address, out ulong imageBase, IntPtr.Zero);
        if (rf == null || imageBase != Base) return (0, 0);
        // A function split into parts has chained entries; follow them back to the primary one.
        for (int i = 0; i < 8; i++)
        {
            byte* info = (byte*)(imageBase + rf->UnwindData);
            if (((info[0] >> 3) & 0x4) == 0) break; // UNW_FLAG_CHAININFO
            rf = (RuntimeFunction*)(info + 4 + ((info[2] + 1) & ~1) * 2);
        }
        return (imageBase + rf->Begin, imageBase + rf->End);
    }

    /// <summary>Start of the game function that a call returning to <paramref name="returnAddress"/> came from, or 0.</summary>
    public static ulong Caller(IntPtr returnAddress) =>
        // The return address points after the call, which can be past the end of the caller.
        returnAddress == IntPtr.Zero ? 0 : Function(returnAddress - 1).start;

    /// <summary>Whether the game function at <paramref name="code"/> contains a direct call to <paramref name="target"/>.</summary>
    public static bool Calls(IntPtr code, IntPtr target)
    {
        var (start, end) = Function(code);
        for (ulong p = start; p + 5 <= end; p++)
        {
            // call rel32
            if (*(byte*)p == 0xE8 && p + 5 + (ulong)(long)*(int*)(p + 1) == (ulong)target) return true;
        }
        return false;
    }

    /// <summary>Address as an offset into GameAssembly.dll (matches Cpp2IL's RVA), for logs.</summary>
    public static string Rva(ulong address) => address >= Base ? $"0x{address - Base:X}" : $"0x{address:X}(abs)";
}
