using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

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

    private ushort _hz = 60;

    // ulong -> 64 bit, 32x64
    private ulong[] _display = new ulong[32];

    // registers V0 to VF
    private byte[] _v = new byte[16];

    // index register
    private ushort _i;

    // program counter
    private ushort _pc = 0x200;
    private ushort[] _stack = new ushort[16];

    // stackpointer
    private byte _sp = 0;

    // timers
    private byte _delayTimer = 0;
    private byte _soundTimer = 0;

    private bool[] _keys = new bool[16];

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
            if (i % 16 == 0)
                Console.Write("\n");

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
        double cycleDelayMs = 1000.0 / _hz;
        var sw = Stopwatch.StartNew();

        while (true)
        {
            var inst = Fetch();
            if (inst is null)
                break;

            DecodeAndExecute(inst);
            UpdateTimers();

            var elapsed = sw.Elapsed.TotalMilliseconds;
            var sleepTime = cycleDelayMs - elapsed;
            if (sleepTime > 0)
            {
                Thread.Sleep((int)sleepTime);
            }

            sw.Restart();
        }
    }

    private Instruction Fetch()
    {
        if (_pc + 1 >= _memory.Length)
            return null;

        var firstByte = _memory[_pc++];
        var secondByte = _memory[_pc++];
        return new Instruction(firstByte, secondByte);
    }

    private void UpdateTimers()
    {
        if (_delayTimer > 0)
            _delayTimer--;
        if (_soundTimer > 0)
            _soundTimer--;
    }

    public void DecodeAndExecute(Instruction inst)
    {
        switch (inst)
        {
            // clear screen
            case { Opcode: 0x00E0 }:
                ClearScreen();
                Render();
                break;
            // returning subroutine
            case { Opcode: 0x00EE }:
                Ret();
                break;
            // JUMP
            // simply set PC to NNN
            case { Indicator: 0x1 }:
                _pc = inst.NNN;
                break;
            case { Indicator: 0x2 }:
                CallSubroutine(inst);
                break;
            // skip conditions
            case { Indicator: 0x3 }:
            case { Indicator: 0x4 }:
            case { Indicator: 0x5 }:
                Skip(inst);
                break;
            // set the register VX to the value NN
            case { Indicator: 0x6 }:
                _v[inst.X] = inst.NN;
                break;
            // add value NN to register VX
            case { Indicator: 0x7 }:
                _v[inst.X] += inst.NN;
                break;
            // logical and arithmetic instructions
            case { Indicator: 0x8 }:
                Operations(inst);
                break;
            case { Indicator: 0x9 }:
                Skip(inst);
                break;
            // set index register I
            case { Indicator: 0xA }:
                _i = inst.NNN;
                break;
            case { Indicator: 0xB }:
                _pc = (ushort)(_v[0] + inst.NNN);
                break;
            // display/draw
            case { Indicator: 0xD }:
                DrawSprite(inst.X, inst.Y, inst.N);
                Render();
                break;
            case { Indicator: 0xE }:
                KeyAction(inst);
                break;
            case { Indicator: 0xF }:
                TimerActions(inst);
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
                Console.Write(pixel ? "██" : "  ");
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

            if (y >= 32)
                break;

            for (int bit = 0; bit < 8; bit++)
            {
                int x = xStart + bit;
                if (x >= 64)
                    break;

                ulong mask = 1UL << (63 - x);

                bool spritePixel = ((spriteByte >> (7 - bit)) & 1) != 0;
                if (!spritePixel)
                    continue;

                bool screenPixel = (_display[y] & mask) != 0;

                // collision detected
                if (screenPixel)
                    _v[0xF] = 1;

                // XOR toggle
                _display[y] ^= mask;
            }
        }
    }

    public void ClearScreen()
    {
        for (int i = 0; i < 32; i++)
            _display[i] = 0UL;
    }

    public void CallSubroutine(Instruction inst)
    {
        _stack[_sp] = _pc;
        _sp++;
        _pc = inst.NNN;
    }

    public void Ret()
    {
        _sp--;
        _pc = _stack[_sp];
    }

    public void Skip(Instruction inst)
    {
        switch (inst)
        {
            case { Indicator: 0x3 }:
                if (_v[inst.X] == inst.NN)
                {
                    _pc += 2;
                }
                break;
            case { Indicator: 0x4 }:
                if (_v[inst.X] != inst.NN)
                {
                    _pc += 2;
                }
                break;
            case { Indicator: 0x5 }:
                if (_v[inst.X] == _v[inst.Y])
                {
                    _pc += 2;
                }
                break;
            case { Indicator: 0x9 }:
                if (_v[inst.X] != _v[inst.Y])
                {
                    _pc += 2;
                }
                break;
            default:
                break;
        }
    }

    // Indicator: 0x8
    public void Operations(Instruction inst)
    {
        switch (inst.N)
        {
            case 0x0000:
                _v[inst.X] = _v[inst.Y];
                break;
            case 0x0001:
                _v[inst.X] |= _v[inst.Y];
                break;
            case 0x0002:
                _v[inst.X] &= _v[inst.Y];
                break;
            case 0x0003:
                _v[inst.X] ^= _v[inst.Y];
                break;
            case 0x0004:
                int sum = _v[inst.X] + _v[inst.Y];
                _v[inst.X] = (byte)(sum & 0xFF);
                _v[0xF] = (byte)(sum > 0xFF ? 1 : 0);
                break;
            case 0x0005:
                byte setVf = (byte)(_v[inst.X] >= _v[inst.Y] ? 1 : 0);
                _v[inst.X] = (byte)((_v[inst.X] - _v[inst.Y]) & 0xFF);
                _v[0xF] = setVf;
                break;
            case 0x0006:
                byte shifted = (byte)(_v[inst.X] & 0x1);
                _v[inst.X] >>= 1;
                _v[0xF] = shifted;
                break;
            case 0x0007:
                byte set = (byte)(_v[inst.Y] >= _v[inst.X] ? 1 : 0);
                _v[inst.X] = (byte)((_v[inst.Y] - _v[inst.X]) & 0xFF);
                _v[0xF] = set;
                break;
            case 0x000E:
                byte shiftedBit = (byte)((_v[inst.X] & 0x80) != 0 ? 1 : 0);
                _v[inst.X] = (byte)((_v[inst.X] << 1) & 0xFF);
                _v[0xF] = shiftedBit;
                break;
        }
    }

    // Indicator: 0xF
    public void TimerActions(Instruction inst)
    {
        switch (inst.NN)
        {
            case 0x07:
                _v[inst.X] = _delayTimer;
                break;
            case 0x15:
                _delayTimer = _v[inst.X];
                break;
            case 0x18:
                _soundTimer = _v[inst.X];
                break;
            case 0x1E:
                _i += _v[inst.X];
                break;
            case 0x33:
                {
                    var value = _v[inst.X];
                    _memory[_i + 2] = (byte)(value % 10);
                    value /= 10;
                    _memory[_i + 1] = (byte)(value % 10);
                    value /= 10;
                    _memory[_i] = (byte)(value % 10);
                }
                break;
            case 0x55:
                for (byte i = 0; i <= inst.X; i++)
                {
                    _memory[_i + i] = _v[i];
                }
                break;
            case 0x65:
                for (byte i = 0; i <= inst.X; i++)
                {
                    _v[i] = _memory[_i + i];
                }
                break;
            case 0x0A:
                bool keyPressed = false;
                for (int i = 0; i < _keys.Length; i++)
                {
                    if (_keys[i])
                    {
                        _v[inst.X] = (byte)i;
                        keyPressed = true;
                        break;
                    }
                }
                if (!keyPressed)
                {
                    _pc -= 2;
                }
                break;
            default:
                break;
        }
    }

    private void KeyAction(Instruction inst)
    {
        switch(inst.NN)
        {
            case 0x9E:
                break;
            case 0xA1:
                break;
            default:
                break;
        }
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
