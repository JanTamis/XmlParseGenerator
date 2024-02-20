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
			FirstName = "Jan Tamis",
			LastName = "Kossen",
			Age = 26,
			Children =
			[
				new Test
				{
					FirstName = "Marry Nel",
					LastName = "Kossen",
					Age = 28,
				},
				new Test
				{
					FirstName = "Marry Nel",
					LastName = "Kossen",
					Age = 28,
				},
			]
		};

		var result = await TestSerializer.SerializeAsync(model);

		var resultObject = await TestSerializer.DeserializeAsync(result);

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
		Children =
		[
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
			new Test
			{
				FirstName = "Marry Nel",
				LastName = "Kossen",
				Age = 28,
			},
		]
	};

	private readonly string _toDeserialize;

	private readonly XmlSerializer _serializer = new(typeof(Test));

	public TestClass()
	{
		_toDeserialize = TestSerializer.Serialize(_test);
	}

	[Benchmark(Baseline = true)]
	public string Default()
	{
		using var builder = new StringWriter();

		_serializer.Serialize(builder, _test);

		return builder.ToString();
	}

	[Benchmark]
	public string DefaultSlow()
	{
		var serializer = new XmlSerializer(typeof(Test));
		using var builder = new StringWriter();

		serializer.Serialize(builder, _test);

		return builder.ToString();
	}

	[Benchmark]
	public string Generator()
	{
		return TestSerializer.Serialize(_test);
	}
}