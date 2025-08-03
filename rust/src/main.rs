use raylib::prelude::*;
use std::fs::{self, File};
use std::io::Read;
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

fn main() {
    let width = 640;
    let height = 480;
    let width_pixel = 64;
    let height_pixel = 32;
    let width_pixel_len = width / width_pixel;
    let height_pixel_len = height / height_pixel;

    let (mut rl, thread) = raylib::init().size(width, height).title("CHIP-8").build();

    let bytes = load_rom("../../roms/ibm.ch8");
    println!("\n\n{} bytes\n", bytes.len());
    let hexdump = get_hexdump(&bytes);
    println!("{}", hexdump);

    let mut chip8_state = Chip8State::new();
    chip8_state.memory[0x200..0x200 + bytes.len()].copy_from_slice(&bytes);
    let chip8 = Arc::new(Mutex::new(chip8_state));

    {
        let chip8 = Arc::clone(&chip8);
        thread::spawn(move || loop {
            {
                let mut state = chip8.lock().unwrap();
                state.cycle();
            }
            thread::sleep(Duration::from_millis(16));
        });
    }

    //let grid_string = get_grid_string(&grid);
    //println!("{}", grid_string);

    while !rl.window_should_close() {
        let mut d = rl.begin_drawing(&thread);

        d.clear_background(Color::BLACK);

        let grid = chip8.lock().unwrap().display.clone();

        for (y, row) in grid.iter().enumerate() {
            for x in 0..64 {
                let bit = (row >> (63 - x)) & 1;
                if bit == 1 {
                    let px = x as i32 * width_pixel_len;
                    let py = y as i32 * height_pixel_len;
                    d.draw_rectangle(px, py, width_pixel_len, height_pixel_len, Color::WHITE);
                }
            }
        }
    }
}

fn load_rom(filename: &str) -> Vec<u8> {
    let mut f = File::open(&filename).expect("no file found");
    let metadata = fs::metadata(&filename).expect("unable to read metadata");
    let mut buffer = vec![0; metadata.len() as usize];
    f.read(&mut buffer).expect("buffer overflow");

    buffer
}

fn get_hexdump(bytes: &[u8]) -> String {
    let bytes_per_line = 10;
    let mut output = String::new();

    for (i, chunk) in bytes.chunks(bytes_per_line).enumerate() {
        output.push_str(&format!("{:04}: ", i * bytes_per_line));

        for (j, b) in chunk.iter().enumerate() {
            output.push_str(&format!("{:02X}", b));

            // formatting twobytes chip8 opcode
            if j % 2 != 0 {
                output.push_str(" ");
            }
        }

        output.push('\n');
    }

    output
}

fn get_empty_grid() -> Vec<u64> {
    vec![0; 32]
}

//fn get_grid_string(grid: &Vec<u64>) -> String {
//    let mut output = String::new();
//    for (i, row) in grid.iter().enumerate() {
//        let line = format!("{:02}: {:064b}\n", i, row);
//        output.push_str(&line);
//    }
//    output
//}

struct Instruction(u16);

impl Instruction {
    fn new(first: u8, second: u8) -> Self {
        Instruction(((first as u16) << 8) | (second as u16))
    }

    fn opcode(&self) -> u16 {
        self.0
    }

    fn indicator(&self) -> u8 {
        ((self.opcode() & 0xF000) >> 12) as u8
    }

    fn x(&self) -> u8 {
        ((self.opcode() & 0x0F00) >> 8) as u8
    }

    fn y(&self) -> u8 {
        ((self.opcode() & 0x00F0) >> 4) as u8
    }

    fn n(&self) -> u8 {
        (self.opcode() & 0x000F) as u8
    }

    fn nn(&self) -> u8 {
        (self.opcode() & 0x00FF) as u8
    }

    fn nnn(&self) -> u16 {
        self.opcode() & 0x0FFF
    }
}

struct Chip8State {
    display: Vec<u64>,
    memory: Vec<u8>,
    v: Vec<u8>,
    pc: u16,
    i: u16,
}

impl Chip8State {
    fn new() -> Self {
        Chip8State {
            display: get_empty_grid(),
            memory: vec![0; 4096],
            v: vec![0; 16],
            pc: 0x200,
            i: 0,
        }
    }

    fn cycle(&mut self) {
        let pc = self.pc as usize;
        let inst = Instruction::new(self.memory[pc], self.memory[pc + 1]);
        self.pc += 2;

        self.decode_and_execute(inst);
    }

    fn decode_and_execute(&mut self, inst: Instruction) {
        match inst.indicator() {
            0x0 => {
                if inst.opcode() == 0x00E0 {
                    //self.display = get_empty_grid();
                }
            }
            0x1 => {}
            0x3 => {
                if self.v[inst.x() as usize] == inst.nn() {
                    self.pc += 2;
                }
            }
            0x4 => {
                if self.v[inst.x() as usize] != inst.nn() {
                    self.pc += 2;
                }
            }
            0x6 => {
                self.v[inst.x() as usize] = inst.nn();
            }
            0x7 => {
                self.v[inst.x() as usize] += inst.nn();
            }
            0x8 => self.v[inst.x() as usize] = self.v[inst.y() as usize],
            0xA => self.i = inst.nnn(),
            0xD => {
                let x_start = self.v[inst.x() as usize] & 63;
                let y_start = self.v[inst.y() as usize] & 31;
                self.v[0xF] = 0;
                for row in 0..inst.n() {
                    let sprite_byte = self.memory[(self.i + row as u16) as usize];
                    let y = y_start + row as u8;

                    if y >= 32 {
                        break;
                    }

                    for bit in 0..8 {
                        let x = x_start + bit;
                        if x >= 64 {
                            break;
                        }
                        let mask = 1u64 << (63 - x);

                        let sprite_pixel = ((sprite_byte >> (7 - bit)) & 1) != 0;
                        if !sprite_pixel {
                            continue;
                        }

                        let screen_pixel = (self.display[y as usize] & mask) != 0;

                        if screen_pixel {
                            self.v[0xF] = 1;
                        }

                        self.display[y as usize] ^= mask;
                    }
                }
            }
            _ => {
                println!("unknown opcode: {:04X}", inst.opcode());
            }
        }
    }
}
