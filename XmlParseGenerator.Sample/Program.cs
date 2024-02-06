using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using BenchmarkDotNet.Attributes;
using XmlParseGenerator.Sample;

public class Program
{
	private async static Task Main()
	{
		var model = new Test
		{
			FirstName = "Jan Tamis",
			LastName = "Kossen",
			Age = 26,
		};

		var result = await TestSerializer.SerializeAsync(model);

		var resultObject = TestSerializer.Deserialize(result);
	}
}

[MemoryDiagnoser]
public class TestClass
{
	private readonly Test _test = new Test
	{
		FirstName = "Jan Tamis",
		LastName = "Kossen",
		Age = 26,
	};
	
	private readonly XmlSerializer _serializer = new(typeof(Test));

	[Benchmark(Baseline = true)]
	public string Default()
	{
		using var builder = new StringWriter();

		_serializer.Serialize(builder, _test);

		return builder.ToString();
	}

	[Benchmark]
	public string Generator()
	{
		return TestSerializer.Serialize(_test);
	}
}