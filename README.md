# 🕹️ CHIP-8 Interpreter in C#

A simple CHIP-8 interpreter written in C# with bit-level handling. No dotnet project files or external dependencies —> just compile and run.

## 🔧 Requirements

- **Mono runtime** (for macOS/Linux)
- **C# compiler:**
  - macOS: `csc` (from .NET SDK or Mono)
  - Linux: `mcs` (Mono C# compiler)

## 🚀 Compile & Run

The program **requires** a CHIP-8 ROM file (`.ch8`) path as a command-line argument.

### On macOS:

```bash
csc Program.cs && mono Program.exe ./roms/ibm.ch8
```

### On Linux:

```bash
mcs Program.cs && mono Program.exe ./roms/ibm.ch8
```
