using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
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
			Children = [new Test()
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			}]
		};

		var result = await TestSerializer.SerializeAsync(model);

		var resultObject = TestSerializer.Deserialize(result);

		BenchmarkRunner.Run<TestClass>();
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
		Children = [new Test()
		{
			FirstName = "Marry Nel",
			LastName = "Kossen",
			Age = 28,
		}]
	};

	private string _toDeserialize;

	private readonly XmlSerializer _serializer = new(typeof(Test));

	public TestClass()
	{
		_toDeserialize = TestSerializer.Serialize(_test);
	}

	[Benchmark(Baseline = true)]
	public Test Default()
	{
		using var builder = new StringReader(_toDeserialize);

		return _serializer.Deserialize(builder) as Test;
	}

	[Benchmark]
	public Test DefaultSlow()
	{
		var serializer = new XmlSerializer(typeof(Test));
		using var builder = new StringReader(_toDeserialize);

		return serializer.Deserialize(builder) as Test;
	}

	[Benchmark]
	public Test Generator()
	{
		return TestSerializer.Deserialize(_toDeserialize);
	}
}