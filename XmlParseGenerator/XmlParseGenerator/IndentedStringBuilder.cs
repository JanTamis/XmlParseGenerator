using System;
using System.Text;

namespace XmlParseGenerator;

public class IndentedStringBuilder
{
	private int _indentLevel;
	private readonly StringBuilder _builder = new();
	private readonly string _defaultIndent;
	private readonly string _indent;

	public IndentedStringBuilder(string defaultIndent, string indent)
	{
		_defaultIndent = defaultIndent;
		_indent = indent;
	}

	public void AppendLine(string value)
	{
		DoIndent();
		_builder.AppendLine(value);
	}

	public void AppendLine()
	{
		DoIndent();
		_builder.AppendLine();
	}

	public void AppendLineWithoutIndent(string value)
	{
		_builder.AppendLine(value);
	}

	public void Append(string value)
	{
		_builder.Append(value);
	}

	public void AppendIndent(string value)
	{
		DoIndent();
		_builder.Append(value);
	}
	
	public void Indent()
	{
		_indentLevel++;
	}
	
	public void Unindent()
	{
		_indentLevel--;
	}
	
	public void SetIndent(int indentLevel)
	{
		_indentLevel = indentLevel;
	}
	
	public IDisposable IndentScope()
	{
		return new IndentDisposable(this);
	}

	public IDisposable IndentScope(string value)
	{
		AppendLine(value);
		return new IndentDisposable(this);
	}

	public IDisposable IndentBlock()
	{
		return new BlockIndentDisposable(this);
	}

	public IDisposable IndentBlock(string value)
	{
		AppendLine(value);
		return new BlockIndentDisposable(this);
	}

	public IDisposable IndentBlockNoNewline()
	{
		return new BlockIndentNoNewLineDisposable(this);
	}

	public IDisposable IndentBlockNoNewline(string value)
	{
		AppendLine(value);
		return new BlockIndentNoNewLineDisposable(this);
	}

	private void DoIndent()
	{
		_builder.Append(_defaultIndent);

		for (var i = 0; i < _indentLevel; i++)
		{
			_builder.Append(_indent);
		}
	}
	
	public override string ToString()
	{
		return _builder.ToString();
	}
	
	public class IndentDisposable : IDisposable
	{
		private readonly IndentedStringBuilder _builder;

		public IndentDisposable(IndentedStringBuilder builder)
		{
			_builder = builder;
			_builder.Indent();
		}

		public void Dispose()
		{
			_builder.Unindent();
		}
	}

	public class BlockIndentDisposable : IDisposable
	{
		private readonly IndentedStringBuilder _builder;

		public BlockIndentDisposable(IndentedStringBuilder builder)
		{
			_builder = builder;
			_builder.AppendLine("{");
			_builder.Indent();
		}

		public void Dispose()
		{
			_builder.Unindent();
			_builder.AppendLine("}");
		}
	}

	public class BlockIndentNoNewLineDisposable : IDisposable
	{
		private readonly IndentedStringBuilder _builder;

		public BlockIndentNoNewLineDisposable(IndentedStringBuilder builder)
		{
			_builder = builder;
			_builder.AppendLine("{");
			_builder.Indent();
		}

		public void Dispose()
		{
			_builder.Unindent();
			_builder.AppendIndent("}");
		}
	}
}