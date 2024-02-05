using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
		};

		// Console.WriteLine(Serializer.Serialize(model));

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
	};
	
	private readonly MemoryStream _stream = new();

	private readonly XmlSerializer _serializer = new(typeof(Test));

	[Benchmark(Baseline = true)]
	public void Default()
	{
		_serializer.Serialize(_stream, _test);
	}

	[Benchmark]
	public void Generator()
	{
		TestSerializer.Serialize(_stream, _test);
	}
}