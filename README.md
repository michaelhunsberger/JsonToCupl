# JsonToCupl

## Overview

JsonToCupl is a tool that aids in translating Verilog to CUPL.  The generated CUPL can then be synthesized by a program called [WinCupl](https://www.microchip.com/en-us/development-tool/WinCUPL).  WinCupl is still available as a free download by Microchip.  Microchip is one of the few manufacturers that still make 5 volt programmable logic.  There appears to be no open-source or affordable applications that can synthesize Verilog for these parts other than using CUPL.

## How it works

JsonToCupl reads JSON generated by Yosys.  Yosys is like a Swiss army knife for RLT synthesis.  One of the many things it can do is export a Verilog design into a JSON document.  Through the export process, higher level language constructs can be converted and optimized into general logic gates. After the export process, CUPL expressions can be generated from the simplified\lower-level representation of the design.  The generated CUPL expressions can then be used within WinCupl to generate a JED for the desired part.

## Requirements

* Any operating system capable of running NET framework 4.0 installed.   JsonToCupl was tested on both XP SP3 and Windows 11.  
* Installed version of [Yosys](https://yosyshq.net/yosys/).  JsonToCupl was tested using version 0.9 for Windows.  The Yosys binaries should be within your environment path.  You can find 0.9 pre-compiled binaries for windows [here](https://github.com/ScoopInstaller/Binary/raw/master/yosys/yosys-win32-mxebin-0.9.zip)
* Installed version of WinCupl. Tested with 5.30.4, but most any version is likely to work.

## Usage

1. Generate Yosys script to export the Verilog design to JSON.  JsonToCupl can auto generate a script for you using its command line parameters.  For example, within the provided UART example you could type this from the command line:
```
JTC -yosys -in uart.v rx.v tx.v uart_clock.v -out uart.ys
```
This creates a proper “.ys” script for Yosys to generate a JSON representation of the UART Verilog code.

2. Run the Yosys script, generating the JSON file.  Following the example above:
```
yosys uart.ys
```
After running this command, a uart.json file is created.

3. Run JsonToCupl again, reading the JSON file and generating the WinCupl code (.PLD file)
```
JTC -in uart.json -device f1504ispplcc44 -module uart -out WinCupl\UART.PLD
```
The above instructs JsonToCupl to target device f1504ispplcc44 (Amtel 1504 CPLD, PLCC44).  I recommend keeping the WinCupl directory separate from were the RTL code lives, as the WinCupl application seems to delete files in the directory where the PLD file is located.

To synthesize the PLD file, just open WinCUPL.  Click File->Open.  Browse to the generated PLD file and open it.  Then click Run->Device Dependent Compile.  If all goes well, you will get a JED file.  
Note:  You can change the device by editing the “device” statement within the PLD file.  A list of device mnemonics is available within WinCupl by going to Options->Devices.

## Command Line Arguments

| Argument | Description |
| -------- | ---------- |
| -yosys | Generate a Yosys file.  This file can then be executed by Yosys to generate a compatible JSON file. |
| -in filenames | Verilog files. |
| -out filename | Yosys output file or generated CUPL.  Yosys scripts should have a ".ys" extension.  CUPL code should have a ".PLD" extension. |
| -pinfile filename | Specifies a pin file.  If omitted, no pin numbers will be assigned to generate CUPL PIN declarations. |
| -device device_name | Specifies a device name.  If omitted, device name defaults to 'virtual'. |
| -module module_name | The module within the json file to process.  If more than one module is defined in the design, this option is required. |
| -combinlimit value | Limits number of buried combinational pinnodes.  If limit is reached, then the required number of pinnodes will be substituted with combinational expressions.  Pinnodes are chosen for expansion based on the least amount of newly created nodes in the expression graph. |

## Examples

Within the source, I included a UART and ripple carry adder example.  In both examples, I was able to generate a JED file and program a chip.  Both was tested electrically as well. 

## Motivation

I wanted to program 5v logic in Verilog, instead of CUPL.  WinCupl is old and clunky--I’ve had little success running it outside of an XP virtual machine.  The language itself is low-level, and the application got more unstable when I tried to use the higher-level features.  So, after banging my head trying to use this archaic software, I went looking for alternatives and eventially came up with this solution.  Its not perfect, but its better than nothing!  I am very grateful that Yosys makes all this possible.

