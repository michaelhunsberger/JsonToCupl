module ripple_carry_adder(input [3:0] a, input [3:0] b, output [3:0] out, output cout);
	wire c1, c2, c3;
	full_adder fa0(a[0], b[0], 0, out[0], c1);
	full_adder fa1(a[1], b[1], c1, out[1], c2);
	full_adder fa2(a[2], b[2], c2, out[2], c3);
	full_adder fa3(a[3], b[3], c3, out[3], cout);
endmodule