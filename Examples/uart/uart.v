/* The following is the top level module for rx and tx */

module uart #(
	parameter FREQUENCY = 11059200,
	parameter BAUD		= 9600
)
( 
	input clk,
	input res_n,
	
	output uart_clk,
	
	/* Rx */
	input         rx, 
	output  [7:0] rx_byte,
	output        rx_rdy,
		
	/* Tx */
	output          tx,
	output			tx_rdy,
	input     [7:0] tx_byte,
	input           stb   /* strobe tx_byte on posedge */
);

	uart_clock #(
		.FREQUENCY(FREQUENCY),
		.BAUD(BAUD)
	) uart_clock_inst (
		.clk(clk),
		.res_n(res_n),
		.uart_clk(uart_clk)
	);
	
	rx rx_inst (
		.res_n(res_n),
		.rx(rx),
		.clk(uart_clk), /* Baud Rate x 4 (4 posedge's per bit) */
		.rx_byte(rx_byte),
		.rdy(rx_rdy)
	);


	tx tx_inst (
		.tx(tx),
		.tx_byte(tx_byte),
		.stb(stb),   /* strobe tx_byte on posedge */
		.res_n(res_n),
		.rdy(tx_rdy),
		.clk(uart_clk)    /* Baud Rate x 4 (same as rx) */ 
	);

endmodule