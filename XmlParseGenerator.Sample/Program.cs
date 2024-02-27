using System.IO;
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
			FirstName = "test",
			LastName = "test",
			Age = 26,
			Children =
			[
				new Test
				{
					FirstName = "test",
					LastName = "test",
					Age = 28,
				},
				new Test
				{
					FirstName = "test",
					LastName = "test",
					Age = 28,
				},
			]
		};

		var result = await TestSerializer.SerializeAsync(model);
		
		var resultObject = await TestSerializer.DeserializeAsync(result);

		BenchmarkRunner.Run<TestClass>();
	}
}

[MemoryDiagnoser(false)]
public class TestClass
{
	private readonly Test _test = new Test
	{
		FirstName = "test",
		LastName = "test",
		Age = 26,
		// Children = Enumerable
		// 	.Range(0, 1_000)
		// 	.Select(s => new Test
		// 	{
		// 		FirstName = "test",
		// 		LastName = "test",
		// 		Age = s,
		// 	})
		// 	.ToList(),
	};

	private readonly string _toDeserialize;

	private readonly XmlSerializer _serializer = new(typeof(Test));

	public TestClass()
	{
		var writer = new StringWriter();
		_serializer.Serialize(writer, _test);
		_toDeserialize = writer.ToString();
	}

	[Benchmark]
	public Test Default()
	{
		using var builder = new StringReader(_toDeserialize);

		return _serializer.Deserialize(builder) as Test;
	}

	// [Benchmark]
	public Test DefaultSlow()
	{
		var serializer = new XmlSerializer(typeof(Test));
		using var builder = new StringReader(_toDeserialize);

		return serializer.Deserialize(builder) as Test;
	}

	[Benchmark(Baseline = true)]
	public Test Generator()
	{
		return TestSerializer.Deserialize(_toDeserialize);
	}
}