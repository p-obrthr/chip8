using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("err: you must provide chip8 rom file path per argument");
            return;
        }

        var chip8 = new Chip8Interpreter(filePath: args[0]);
        chip8.Run();
    }
}

class Chip8Interpreter
{
    private string _filePath = default!;
    private byte[] _romBytes;
    private byte[] _memory = new byte[4096];
    // ulong -> 64 bit, 32x64
    private ulong[] _display = new ulong[32];
    // registers V0 to VF
    private byte[] _v = new byte[16];
    // index register
    private ushort _i;
    // program counter
    private ushort _pc = 0x200; 
    private ushort[] _stack = new ushort[16];
    //private byte _sp;

    public Chip8Interpreter(string filePath)
    {
        _filePath = filePath;
    }

    public void Run()
    {
        LoadRom();
        PrintSpecs();
        WaitForStart();
        Interprete();
    }

    public void LoadRom()
    {
        _romBytes = File.ReadAllBytes(_filePath);
        for (int i = 0; i < _romBytes.Length && i + 0x200 < _memory.Length; i++)
        {
            _memory[i + 0x200] = _romBytes[i];
        }
    }

    public void PrintSpecs()
    {
        Console.WriteLine($"rom size: {_romBytes.Length} bytes");

        // Hexdumping
        for (int i = 0; i < _romBytes.Length; i += 2)
        {
            if (i % 16 == 0) Console.Write("\n");

            var first = $"{_romBytes[i]:X2}";
            var second = i + 1 < _romBytes.Length ? $"{_romBytes[i + 1]:X2}" : "";

            Console.Write($"{first}{second} ");
        }
        Console.Write("\n\n");
    }

    public void WaitForStart()
    {
        Console.WriteLine("Press key to start interpreting...");
        Console.ReadKey();
        Console.Clear();
    }

    public void Interprete()
    {
        while(true)
        {
            var inst = Fetch();
            if (inst is null) break;
            DecodeAndExecute(inst);
            Render();
            Thread.Sleep(300);
        }
    }

    private Instruction Fetch()
    {
        if (_pc + 1 >= _memory.Length) return null;

        var firstByte = _memory[_pc++];
        var secondByte = _memory[_pc++];
        return new Instruction(firstByte, secondByte);
    }

    public void DecodeAndExecute(Instruction inst)
    {
        switch (inst)
        {
            // clear screen
            case { Opcode: 0x00E0 }:
                ClearScreen();
                break;
            // JUMP
            // simply set PC to NNN
            case { Indicator: 0x1}:
                _pc = inst.NNN;
                break;
            // set the register VX to the value NN
            case { Indicator: 0x6}:
                _v[inst.X] = inst.NN;
                break;
            // add value NN to register VX
            case { Indicator: 0x7}:
                _v[inst.X] += inst.NN;
                break;
            // set index register I
            case { Indicator: 0xA}:
                _i = inst.NNN;
                break;
            // display/draw
            case { Indicator: 0xD}:
                DrawSprite(inst.X, inst.Y, inst.N);
                break;
            default:
                break;
        }
    }

    private void Render()
    {
        Console.SetCursorPosition(0, 0);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bool pixel = (_display[y] & (1UL << (63 - x))) != 0;
                Console.Write(pixel ? "█" : " ");
            }
            Console.Write("\n");
        }
    }
    
    private void DrawSprite(byte vx, byte vy, byte height)
    {
        int xStart = _v[vx] & 63;
        int yStart = _v[vy] & 31;

        // reset collision flag
        _v[0xF] = 0;

        for (int row = 0; row < height; row++)
        {
            byte spriteByte = _memory[_i + row];
            int y = yStart + row;

            if (y >= 32) break;

            for (int bit = 0; bit < 8; bit++)
            {
                int x = xStart + bit;
                if (x >= 64) break;

                ulong mask = 1UL << (63 - x);

                bool spritePixel = ((spriteByte >> (7 - bit)) & 1) != 0;
                if (!spritePixel) continue;

                bool screenPixel = (_display[y] & mask) != 0;

                // collision detected
                if (screenPixel) _v[0xF] = 1;

                // XOR toggle
                _display[y] ^= mask;
            }
        }
    }


    public void ClearScreen()
    {
        for (int i = 0; i < 32; i++) _display[i] = 0UL;
    }
}

class Instruction
{
    // two concenated bytes -> four nibbles 
    public ushort Opcode { get; private set; } 

    // first nibble extraction
    public byte Indicator => (byte)((Opcode & 0xF000) >> 12); 

    // second nibble extraction
    public byte X => (byte)((Opcode & 0x0F00) >> 8);
    // bit manipulation exampel on X
    // 0x6AB3 = 0110 1010 1011 0011
    // 0x0F00 = 0000 1111 0000 0000
    // AND
    // ----------------------------
    //          0000 1010 0000 0000
    // >> 8   ➜ 0000 0000 0000 1010 = 0xA = 10

    // third nibble 
    public byte Y => (byte)((Opcode & 0x00F0) >> 4);
    // fourth nibble
    public byte N => (byte)(Opcode & 0x000F);
    // second + third
    public byte NN => (byte)(Opcode & 0x00FF);
    // second + third + fourth nibble
    public ushort NNN => (ushort)(Opcode & 0x0FFF);

    public Instruction(byte first, byte second)    
    {
        Opcode = (ushort)(first << 8 | second);
    }
}
