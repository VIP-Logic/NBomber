module FSharp.MongoDb.MongoDbTest

open System
open System.Threading
open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine
open MongoDB.Bson
open MongoDB.Bson.Serialization.Attributes

open MongoDB.Driver
open NBomber.Contracts
open NBomber.FSharp

type User = {
    [<BsonId>]
    Id: ObjectId
    Name: string
    Age: int
    IsActive: bool
}

let run () =

    let scenarioInit (context: IScenarioContext) = task {
        let db = MongoClient().GetDatabase("Test")

        let testData =
            [0..2000]
            |> Seq.map(fun i -> { Id = ObjectId.GenerateNewId()
                                  Name = sprintf "test user %i" i
                                  Age = i
                                  IsActive = true })
            |> Seq.toList

        db.DropCollection("Users", context.CancellationToken)

        do! db.GetCollection<User>("Users")
              .InsertManyAsync(testData, cancellationToken = context.CancellationToken)
    }

    let db = MongoClient().GetDatabase("Test")
    let usersCollection = db.GetCollection<User>("Users")

    let step = Step.create("query_users", fun context -> task {

        let! response =
            usersCollection.Find(fun u -> u.IsActive)
                           .Limit(Nullable<int> 500)
                           .ToListAsync()

        return Response.Ok();
    })

    Scenario.create "mongo_scenario" [step]
    |> Scenario.withInit scenarioInit
    |> Scenario.withLoadSimulations [KeepConstant(copies = 100, during = seconds 30)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "mongo"
    |> NBomberRunner.withTestName "simple_query_test"
    |> NBomberRunner.run
    |> ignore
