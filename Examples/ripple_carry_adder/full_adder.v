module full_adder(input a, input b, input carry, output out, output cout);
	assign out = a ^ b ^ carry;
	assign cout = ((a ^ b) & carry) | (a & b);
endmodule