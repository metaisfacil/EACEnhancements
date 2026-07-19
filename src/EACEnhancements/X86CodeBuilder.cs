using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal sealed class X86CodeBuilder
    {
        private readonly List<byte> bytes = new List<byte>();
        private readonly uint baseAddress;

        internal X86CodeBuilder(IntPtr address)
        {
            baseAddress = unchecked((uint)address.ToInt32());
        }

        internal int Offset
        {
            get { return bytes.Count; }
        }

        internal uint AddressOf(int offset)
        {
            return baseAddress + unchecked((uint)offset);
        }

        internal byte[] ToArray()
        {
            return bytes.ToArray();
        }

        internal void Emit(byte[] data)
        {
            bytes.AddRange(data);
        }

        internal void EmitJmp(uint target)
        {
            EmitRelative(0xE9, target, 5);
        }

        internal void EmitCall(uint target)
        {
            EmitRelative(0xE8, target, 5);
        }

        internal void EmitJz(uint target)
        {
            int start = Offset;
            bytes.Add(0x0F);
            bytes.Add(0x84);
            EmitInt32(RelativeDisplacement(start, 6, target));
        }

        internal void EmitJnz(uint target)
        {
            int start = Offset;
            bytes.Add(0x0F);
            bytes.Add(0x85);
            EmitInt32(RelativeDisplacement(start, 6, target));
        }

        internal int EmitJzPlaceholder()
        {
            int start = Offset;
            bytes.Add(0x0F);
            bytes.Add(0x84);
            EmitInt32(0);
            return start;
        }

        internal void PatchBranch(int instructionOffset, uint target)
        {
            int displacement = RelativeDisplacement(instructionOffset, 6, target);
            byte[] value = BitConverter.GetBytes(displacement);
            for (int index = 0; index < 4; index++)
                bytes[instructionOffset + 2 + index] = value[index];
        }

        internal void EmitPushImmediate(uint value)
        {
            bytes.Add(0x68);
            EmitUInt32(value);
        }

        internal void EmitPushAbsolute(uint address)
        {
            bytes.Add(0xFF);
            bytes.Add(0x35);
            EmitUInt32(address);
        }

        internal void EmitMovByteAbsolute(uint address, byte value)
        {
            bytes.Add(0xC6);
            bytes.Add(0x05);
            EmitUInt32(address);
            bytes.Add(value);
        }

        internal void EmitCmpByteAbsolute(uint address, byte value)
        {
            bytes.Add(0x80);
            bytes.Add(0x3D);
            EmitUInt32(address);
            bytes.Add(value);
        }

        internal void EmitCmpDwordAbsoluteImmediate8(uint address, byte value)
        {
            bytes.Add(0x83);
            bytes.Add(0x3D);
            EmitUInt32(address);
            bytes.Add(value);
        }

        internal void EmitIncrementDwordAbsolute(uint address)
        {
            bytes.Add(0xFF);
            bytes.Add(0x05);
            EmitUInt32(address);
        }

        internal void EmitCopyBytesPreservingRegisters(uint source, uint destination, int count)
        {
            bytes.Add(0x60);                              // PUSHAD
            bytes.Add(0xFC);                              // CLD
            bytes.Add(0xBE);                              // MOV ESI, source
            EmitUInt32(source);
            bytes.Add(0xBF);                              // MOV EDI, destination
            EmitUInt32(destination);
            bytes.Add(0xB9);                              // MOV ECX, count
            EmitUInt32(unchecked((uint)count));
            bytes.Add(0xF3);
            bytes.Add(0xA4);                              // REP MOVSB
            bytes.Add(0x61);                              // POPAD
        }

        private void EmitRelative(byte opcode, uint target, int length)
        {
            int start = Offset;
            bytes.Add(opcode);
            EmitInt32(RelativeDisplacement(start, length, target));
        }

        private int RelativeDisplacement(int instructionOffset, int instructionLength, uint target)
        {
            long next = (long)baseAddress + instructionOffset + instructionLength;
            long displacement = (long)target - next;
            if (displacement < Int32.MinValue || displacement > Int32.MaxValue)
                throw new InvalidOperationException("Generated workflow branch is outside rel32 range.");
            return unchecked((int)displacement);
        }

        private void EmitUInt32(uint value)
        {
            bytes.AddRange(BitConverter.GetBytes(value));
        }

        private void EmitInt32(int value)
        {
            bytes.AddRange(BitConverter.GetBytes(value));
        }
    }

}
