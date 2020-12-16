﻿// using System;
// using System.Data;
// using System.Threading.Tasks;
// using Microsoft.Extensions.Configuration;
// using Microsoft.FSharp.Core;
// using NBomber.Contracts;
// using NBomber.CSharp;
// using Serilog;
//
// namespace CSharp.Plugin
// {
//     public class CustomHtmlPlugin: IWorkerPlugin
//     {
//         private const string Header = "<script>console.log('custom header');</script>";
//         private const string Js = "console.log('custom js');";
//         private const string ViewModel = "{ message: 'Hello from custom html' }";
//         private const string HtmlTemplate = "<h3>Message: {{viewModel.message}}</h3>";
//
//         public string PluginName => "CustomHtml";
//
//         public void Init(ILogger logger, FSharpOption<IConfiguration> infraConfig)
//         {
//         }
//
//         public Task Start(TestInfo testInfo) => Task.CompletedTask;
//
//         public DataSet GetStats(NodeOperationType currentOperation)
//         {
//             var pluginStats = new DataSet();
//
//             if (currentOperation == NodeOperationType.Complete)
//             {
//                 var table = CustomPluginDataBuilder
//                     .Create("Custom html")
//                     .WithHeader(Header)
//                     .WithJs(Js)
//                     .WithViewModel(ViewModel)
//                     .WithHtmlTemplate(HtmlTemplate)
//                     .Build();
//
//                 pluginStats.Tables.Add(table);
//             }
//
//             return pluginStats;
//         }
//
//         public string[] GetHints() => Array.Empty<string>();
//
//         public Task Stop() => Task.CompletedTask;
//
//         public void Dispose()
//         {
//         }
//     }
//
//     public class CustomHtmlExample
//     {
//         public static void Run()
//         {
//             var step1 = Step.Create("step_1", async context =>
//             {
//                 await Task.Delay(TimeSpan.FromSeconds(0.1));
//                 return Response.Ok();
//             });
//
//             var scenario = ScenarioBuilder
//                 .CreateScenario("scenario_1", step1)
//                 .WithoutWarmUp()
//                 .WithLoadSimulations(
//                     Simulation.KeepConstant(copies: 1, during: TimeSpan.FromSeconds(30))
//                 );
//
//             NBomberRunner
//                 .RegisterScenarios(scenario)
//                 .WithWorkerPlugins(new CustomHtmlPlugin())
//                 .WithTestSuite("custom_html")
//                 .WithTestName("simple_test")
//                 .Run();
//         }
//     }
// }
