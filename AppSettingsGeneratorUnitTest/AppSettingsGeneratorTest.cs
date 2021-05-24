using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using additiv.Caching.Redis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Xunit.Abstractions;

namespace AppSettingsGeneratorUnitTest
{
   public class AppSettingsGeneratorTest
    {
        private readonly ITestOutputHelper _outputHelper;

        public AppSettingsGeneratorTest(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }
        private static Compilation CreateCompilation(string source)
        {
            var references =
                (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                    where !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location)
                    select MetadataReference.CreateFromFile(assembly.Location)).ToList();
            references.Add(MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location));
            return CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9)) },
                references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        }

        [Fact]
        public void Test()
        {
            Compilation inputCompilation = CreateCompilation(@"
using System;
using System.ComponentModel;
{
public class Program 
{
     public static void Main(string[] args)
    {
        var rc = new additiv.Caching.Redis.RedisConfiguration();
        var evo = new AppSettingsGeneratorUnitTest.EvoPdfConfiguration();
    }
}
 

");
            var rd = new RedisConfiguration();
            List<AdditionalText> additionalTexts = new List<AdditionalText>();
            var additionalTextPaths = new List<string> {"appsettings.json"};

            foreach (string additionalTextPath in additionalTextPaths)
            {
                AdditionalText additionalText = new CustomAdditionalText(additionalTextPath);
                additionalTexts.Add(additionalText);
            }

            var generator = new AppSettingsGenerator.AppSettingsGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.AddAdditionalTexts(ImmutableArray.CreateRange(additionalTexts));
            driver = driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation,
                out var diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            if (runResult.Diagnostics.Length > 0)
            {
                foreach (var diag in runResult.Diagnostics)
                {
                    _outputHelper.WriteLine(diag.GetMessage());
                }
            }
            else
            {
                string sourceGenerated = runResult.Results[0].GeneratedSources[0].SourceText.ToString() ?? string.Empty;
                _outputHelper.WriteLine(sourceGenerated);
            }


        }

    }

   public class CustomAdditionalText : AdditionalText
   {
       private readonly string _text;

       public override string Path { get; }

       public CustomAdditionalText(string path)
       {
           Path = path;
           _text = File.ReadAllText(path);
       }

       public override SourceText GetText(CancellationToken cancellationToken = new CancellationToken())
       {
           return SourceText.From(_text);
       }
   }

}