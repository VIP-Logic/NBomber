using System;
using System.Linq;
using System.Threading.Tasks;
using NBomber;
using NBomber.Contracts;
using NBomber.CSharp;
using Serilog;

namespace CSharp.Examples
{
    class DataFeedScenario
    {
        public static void Run()
        {
            var feed =
                Feed
                    .Circular("numbers", Enumerable.Range(0, 10))
                    .Select(x => (x + 1) * 10)
                    .Select(x => x.ToString());

            var step = Step.Create("step", async context =>
            {
                // you can do any logic here: go to http, websocket etc

                await Task.Delay(TimeSpan.FromSeconds(0.1));
                var number = (string)context.Data["numbers"];
                Log.Information("Data from feed: {number}", number);
                return Response.Ok();
            });

            var tryNextTime = "Feeds to try as next";
            var rnd = new Random();
            if (string.IsNullOrWhiteSpace(tryNextTime))
            {
                var nop = Feed.Empty<int>();
                var seq = Feed.Sequence("index", new[] {1,2,3});
                var cir = Feed.Circular("index", new[] {1,2,3});
                var sfl = Feed.Shuffle("index", new[] {1,2,3});
                var csv = Feed.FromJson<User>("user", "C:/Files/users.json");
            }

            var scenario = ScenarioBuilder
                .CreateScenario("Hello World!", new[] { step })
                .WithFeed(feed);

            NBomberRunner.RegisterScenarios(scenario)
                         .RunInConsole();
        }

        public class User
        {
            public long Id { get; set; }
            public string Name { get; set; }
        }
    }
}
