module $_DLATCH_P_(input E, input D, output Q);
    LDCP _TECHMAP_REPLACE_ (
        .D(D),
        .G(E),
        .Q(Q),
        .PRE(1'b0),
        .CLR(1'b0)
        );
endmodule
