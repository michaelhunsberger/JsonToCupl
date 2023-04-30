`timescale 1ns/1ns

/* Simple test for uart.v  Transmits a bit sequence to uart tx wire and echos back the recieved 8 bits */

module uart_echo_test();

reg clk;
reg res_n;
reg rx; 

wire [0:7] rx_byte;
wire rx_rdy;
wire uart_clk;
wire tx_rdy;
wire tx;
 

parameter BAUD			= 9600;
parameter FREQUENCY 	= 11059200;
parameter CLK_NS		= 45; //(10^9)*(1/ (FREQUENCY * 2))
parameter BIT_RATE_NS 	= 104167; //(10^9) * (1 / BAUD);

initial
 begin
    $dumpfile("uart_echo_test.vcd");
    $dumpvars(0,uart_echo_test);
 end
 
initial
begin 
	clk <= 1'b0; 
	forever begin
	  #(CLK_NS);
	  clk <= ~clk;
	end
end

initial
begin
	$display("test: BAUD=%d", BAUD);
	$display("test: FREQUENCY=%d", FREQUENCY);
	$display("test: CLK_NS=%d", CLK_NS);
	$display("test: BIT_RATE_NS=%d", BIT_RATE_NS);
	rx <= 1'b1;
	#(BIT_RATE_NS);
	res_n = 1'b0; 
	#(BIT_RATE_NS);
	res_n = 1'b1;  
	#(BIT_RATE_NS);

	#(BIT_RATE_NS);
	$display("test: start bit");
	rx = 1'b0;      /* start bit */
	#(BIT_RATE_NS);
	$display("test: 1");
	rx = 1'b0;      /* 1 */
	#(BIT_RATE_NS);
	$display("test: 2");  
	rx = 1'b0;      /* 2 */
	#(BIT_RATE_NS);
	$display("test: 3");
	rx = 1'b1;      /* 3 */
	#(BIT_RATE_NS);
	$display("test: 4");
	rx = 1'b1;      /* 4 */
	#(BIT_RATE_NS);
	$display("test: 5");
	rx = 1'b0;      /* 5 */
	#(BIT_RATE_NS);
	$display("test: 6");
	rx = 1'b0;      /* 6 */
	#(BIT_RATE_NS);
	$display("test: 7");
	rx = 1'b0;      /* 7 */
	#(BIT_RATE_NS);
	$display("test: 8");
	rx = 1'b1;      /* 8 */
	#(BIT_RATE_NS);
	$display("test: 9");
	rx = 1'b1;      /* end bit */
	#(BIT_RATE_NS * 40);
	$finish;
end

uart #(
	.FREQUENCY(FREQUENCY),
	.BAUD(BAUD)
) uart_inst ( 
	.clk(clk),
	.res_n(res_n),
	
	/* Rx */
	.rx(rx), 
	.rx_byte(rx_byte),
	.rx_rdy(rx_rdy),
	
	
	/* Tx */
	.tx(tx),
	.tx_rdy(tx_rdy),

	
	.uart_clk(uart_clk),
	.tx_byte(rx_byte),
	.stb(rx_rdy)   /* strobe tx_byte on posedge */
);

always @ (posedge rx_rdy)
begin
	$display("rx_rdy = %x", rx_byte);
end

always @ (posedge uart_clk)
begin
	$display("uart_clk = %x", uart_clk);
end

always @ (posedge tx or negedge tx)
begin
	$display("tx: out = %x", tx);
end


endmodule