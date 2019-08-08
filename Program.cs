using BenchmarkDotNet.Attributes;
using System.IO;

namespace MSerra.Net
{
    class Program
    {
        static void Main(string[] args)
        {
            var bench = BenchmarkDotNet.Running.BenchmarkRunner.Run<BenchmarkJsonDeserialization>();

            //var b = new BenchmarkJsonDeserialization();
            //b.Setup();

            //b.JsonMaxParser();
            //b.NewtonsoftJson();
            //b.SystemTextJson();

        }
    }

    [MemoryDiagnoser]
    //[InliningDiagnoser]
    public class BenchmarkJsonDeserialization
    {
        private Newtonsoft.Json.JsonReader jsonReader;
        private StreamReader streamReader;
        private Stream stream;

        [IterationSetup]
        public void Setup()
        {
            var json = File.ReadAllText(@"C:\git\jsonmaxparser\random.json");

            // JsonMaxParser setup
            var s = File.OpenRead(@"C:\git\jsonmaxparser\random.json");
            streamReader = new StreamReader(s);

            //Newtonsoft setup
            var tr = new StringReader(json);
            jsonReader = new Newtonsoft.Json.JsonTextReader(tr);

            // SystemTextJson setup
            stream = File.OpenRead(@"C:\git\jsonmaxparser\random.json");
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
