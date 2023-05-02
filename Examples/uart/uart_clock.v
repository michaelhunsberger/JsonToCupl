/* Clock divider for the uart module */

module uart_clock #(
	parameter FREQUENCY = 11059200,
	parameter BAUD		= 9600
)
(
	input wire clk, //system clock
	input wire res_n, //reset
	output reg uart_clk //uart clock
);

	//parameter UART_CLK_DIV = FREQUENCY / (2 * BAUD * 4); //4 clocks per bit sample
	parameter UART_CLK_DIV = FREQUENCY / (2 * BAUD * 4); //4 clocks per bit sample
	parameter CNT_REG_SIZE = $clog2(UART_CLK_DIV);
	
	reg [CNT_REG_SIZE - 1: 0] counter;
	
	always @ (posedge clk or negedge res_n) 
	begin 
		if (!res_n) begin
			counter <= 1'b0;
			uart_clk <= 1'b0;
		end
		else if (counter == UART_CLK_DIV[CNT_REG_SIZE-1:0]) begin
			counter <= 1'b0;
			uart_clk <= ~uart_clk;
		end
		else begin 
			counter <= counter + 1'b1;
		end
	end
endmodule
