using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using System.IO;
using System.Linq;

namespace MSerra.Net
{
    class Program
    {
        static void Main(string[] args)
        {
            var bench = BenchmarkDotNet.Running.BenchmarkRunner.Run<BenchmarkJsonDeserialization>();

            // var b = new BenchmarkJsonDeserialization();
            // b.Setup();

            // b.JsonMaxParser();
            // b.NewtonsoftJson();
            // b.SystemTextJson();

        }
    }

    [MemoryDiagnoser]
    [InliningDiagnoser]
    public class BenchmarkJsonDeserialization
    {
        private Newtonsoft.Json.JsonReader jsonReader;
        private StreamReader streamReader;
        private Stream stream;

        [IterationSetup]
        public void Setup()
        {
            var filepath = @"C:\git\jsonmaxparser\random.json";

            var json = File.ReadAllText(filepath);

            // JsonMaxParser setup
            var s = File.OpenRead(filepath);
            streamReader = new StreamReader(s);

            //Newtonsoft setup
            var tr = new StringReader(json);
            jsonReader = new Newtonsoft.Json.JsonTextReader(tr);

            // SystemTextJson setup
            stream = File.OpenRead(filepath);
        }

        [Benchmark]
        public void JsonMaxParser()
        {
            var o = new JsonMaxParser().Parse(streamReader);
        }

        [Benchmark]
        public void NewtonsoftJson()
        {
            var o = Newtonsoft.Json.Linq.JObject.Load(jsonReader);
        }

        [Benchmark]
        public void SystemTextJson()
        {
            var o = System.Text.Json.JsonDocument.Parse(stream);
        }
    }
}
