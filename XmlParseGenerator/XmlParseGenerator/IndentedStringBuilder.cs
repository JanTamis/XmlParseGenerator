using System.Text;

namespace XmlParseGenerator;

public class IndentedStringBuilder
{
	private int _indentLevel;
	private readonly StringBuilder _builder = new();
	private readonly string _defaultIndent;

	public IndentedStringBuilder(string defaultIndent)
	{
		_defaultIndent = defaultIndent;
	}

	public void AppendLine(string value)
	{
		_builder.Append(_defaultIndent);
		_builder.Append(new string('\t', _indentLevel));
		_builder.AppendLine(value);
	}

	public void AppendLine()
	{
		_builder.Append(_defaultIndent);
		_builder.Append(new string('\t', _indentLevel));
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
		_builder.Append(_defaultIndent);
		_builder.Append(new string('\t', _indentLevel));
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

	public override string ToString()
	{
		return _builder.ToString();
	}
}