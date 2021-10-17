// JsonToCupl.CodeGen
using JsonToCupl;
using System;
using System.Collections.Generic;

internal class CodeGen
{
	 class Pinnode
	{
		public Node Node;

		public string Name;
	}

	class CodeGenCtx
	{
		public List<Node> CreatedPinNodes = new List<Node>();
	}

	readonly ContainerNode _mod;

	readonly HashSet<Node> _visited = new HashSet<Node>();

	public CodeGen(ContainerNode mod)
	{
		_mod = mod;
	}

	public void Test()
	{
		_visited.Clear();
		CodeGenCtx ctx = new CodeGenCtx();
		CreatePinNodes(_mod, ctx);
		foreach (Node pinNode in ctx.CreatedPinNodes)
		{
			foreach (PinConnection pinNodeCon in pinNode.Connections)
			{
				if (pinNodeCon.DirectionType == DirectionType.Input)
				{
					_mod.Connections.Add(pinNodeCon);
				}
			}
		}
		_visited.Clear();
		GenerateExpressions(_mod, ctx);
	}

	void GenerateExpressions(ContainerNode node, CodeGenCtx ctx)
    {
		foreach(var con in node.Connections)
        {
			if (con.DirectionType == DirectionType.Input || con.DirectionType == DirectionType.Inout)
			{
				var refs = con.Refs;
				if (refs.Count == 0)
					continue; //should not happen
				if (refs.Count != 1)
					throw new ApplicationException($"Invalid input reference count at {con.Name}");

				Console.Write(con.Name + " = ");
				GenerateComboLogic(refs[0], ctx);
				Console.WriteLine(";");
			}
		}
    }

	void GenerateComboLogic(PinConnection outputNode, CodeGenCtx ctx)
	{
		Node parentNode = outputNode.Parent;
		switch (parentNode.Type)
		{
			case NodeType.DFF:
			case NodeType.TBUF:
				Console.Write(parentNode.Name + "." + outputNode.Name);
				return; //Don't walk though none combination logic elements like flip flops
			case NodeType.PinNode:
				Console.Write(parentNode.Name);
				return; //Don't walk though pinnodes
			case NodeType.Module:
				Console.Write(outputNode.Name);
				return;
		}
		if (_visited.Contains(parentNode))
			return;
		_visited.Add(parentNode);

		if (parentNode.Type == NodeType.Not)
		{
			Console.Write("! ( ");
		}
		else
		{
			Console.Write(" ( ");
		}

		bool didWriteOperator = false;
		foreach (var con in parentNode.Connections)
		{
			if (con.DirectionType == DirectionType.Input || con.DirectionType == DirectionType.Inout)
			{
				var refs = con.Refs;
				if (refs.Count == 0)
					continue; //should not happen
				if (refs.Count != 1)
					throw new ApplicationException($"Invalid input reference count at {con.Name}");

				GenerateComboLogic(con.Refs[0], ctx);
				if (!didWriteOperator)
				{
					switch (parentNode.Type)
					{
						case NodeType.And:
							Console.Write(" & ");
							break;
						case NodeType.Or:
							Console.Write(" | ");
							break;
						case NodeType.Xor:
							Console.Write(" # ");
							break;
						case NodeType.Not:
							break; //Not a binary operator
						default:
							throw new ApplicationException($"Unknown combinational operator type '{parentNode.Type}'");
					}
					didWriteOperator = true;
				}
			}
		}
		Console.Write(" )");
	}

	void CreatePinNodes(Node node, CodeGenCtx ctx)
	{
		if (node.Type != NodeType.PinNode && !_visited.Contains(node))
		{
			_visited.Add(node);
			foreach (PinConnection connection in node.Connections)
			{
				if (connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Inout)
				{
					List<PinConnection> refs = connection.Refs;
					if (refs.Count != 0)
					{
						if (refs.Count != 1)
						{
							throw new ApplicationException("Invalid number of connections to input node " + connection.Name);
						}
						PinConnection output = connection.Refs[0];
						if (output.DirectionType != DirectionType.Output && output.DirectionType != DirectionType.Inout)
						{
							throw new ApplicationException("Unknown connection to input node " + connection.Name);
						}
						Node parentOutputNode = output.Parent;
						if (output.Refs.Count > 1 || parentOutputNode.Type == NodeType.DFF)
						{
							string newName = Util.GenerateName();
							Node pinNode = new Node(newName, NodeType.PinNode);
							PinConnection outputForPinNode = new PinConnection(pinNode, Util.GenerateName(), DirectionType.Output);
							PinConnection inputForPinNode = new PinConnection(pinNode, Util.GenerateName(), DirectionType.Input);
							pinNode.Connections.Add(outputForPinNode);
							pinNode.Connections.Add(inputForPinNode);
							foreach (PinConnection inputNodeThatRefsOutput in output.Refs)
							{
								if (inputNodeThatRefsOutput.Refs.Count > 1 || (inputNodeThatRefsOutput.DirectionType != DirectionType.Input && inputNodeThatRefsOutput.DirectionType != DirectionType.Inout))
								{
									throw new ApplicationException("Output node references non input node");
								}
								inputNodeThatRefsOutput.Refs.Clear();
								inputNodeThatRefsOutput.Refs.Add(outputForPinNode);
							}
							output.Refs.Clear();
							output.Refs.Add(inputForPinNode);
							inputForPinNode.Refs.Add(output);
							ctx.CreatedPinNodes.Add(pinNode);
						}
						CreatePinNodes(parentOutputNode, ctx);
					}
				}
			}
		}
	}
}
