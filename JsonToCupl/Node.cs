using System;
using System.Collections.Generic;

namespace JsonToCupl
{
    enum NodeType
    {
        Unknown,
        Module,
        And,
        Or,
        Xor,
        Not,
        DFF,
        Latch,
        TBUF,
        Constant,
        Pin,
        PinNode
    }

    [Flags]
    enum NodeProcessState
    {
        None = 0,
        MergeRegister = (1 << 0),
        MergeTBUF = (1 << 1),
    }

    /// <summary>
    /// TODO: In hindsight, I think another design would have been better.  Instead of representing the node graph like this, 
    /// a better way would have been a Graph class with a dictionary inConnection\outConnection style data structure.
    /// The graph is a multigraph/bidirectional graph.  This gets the job done though.
    /// </summary>
    class Node 
    {
        public Node(string name, NodeType type, int constant = 0)
        {
            Name = name;
            Type = type;
            Constant = constant;
        }

        public NodeProcessState NodeProcessState { get; set; } = NodeProcessState.None;
        public string Name { get; set; }
        public Connections Connections { get; } = new Connections();
        public NodeType Type { get; }
        public int Constant { get; }
        
        /// <summary>
        /// Recursive depth of the node.
        /// 
        /// For example, 
        /// A := (C & D) | (E # F)
        /// A would have a complexity of 4
        /// & would have a complexity of 2
        /// # would have a complexity of 2
        /// | would have a complexity of 4
        /// </summary>
        public int Complexity { get; set; }

        /// <summary>
        /// The output complexity.  Its the complexity * (how many input connections reference its output)
        /// </summary>
        public int OutputComplexity
        {
            get
            {
                return Complexity * Connections.GetOutputOrBidirectional().Refs.Count;
            }
        }
    }
}
