﻿using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace NBomber.Examples.CSharp.Scenarios.Mongo
{
    class MongoScenario
    {
        public static Scenario Build()
        {
            var db = new MongoClient().GetDatabase("Test");
            var usersCollection = db.GetCollection<User>("Users");

            var testData = Enumerable.Range(0, 2000)
                                     .Select(i => new User { Name = $"Test User {i}", Age = i, IsActive = true })
                                     .ToList();

            Func<Task<StepResult>> initDb = async () =>
            {
                db.DropCollection("Users");
                await usersCollection.InsertManyAsync(testData);
                return StepResult.Ok;
            };

            var readQuery1 = usersCollection.Find(u => u.IsActive == true).Limit(500);
            var readQuery2 = usersCollection.Find(u => u.Age > 50).Limit(100);

            var step1 = Step.Create("read IsActive = true and TOP 500", async () =>
            {
                await readQuery1.ToListAsync();
                return StepResult.Ok;
            });

            var step2 = Step.Create("read Age > 50 and TOP 100", async () =>
            {
                await readQuery1.ToListAsync();
                return StepResult.Ok;
            });

            return new ScenarioBuilder(scenarioName: "Test MongoDb with 2 READ quries and 2000 docs")
                .Init(initDb)
                .AddTestFlow("READ Users 1", steps: new[] { step1 }, concurrentCopies: 20)
                .AddTestFlow("READ Users 2", steps: new[] { step2 }, concurrentCopies: 20)
                .Build(interval: TimeSpan.FromSeconds(10));
        }
    }

    public class User
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}
