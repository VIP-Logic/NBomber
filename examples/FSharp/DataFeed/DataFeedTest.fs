module FSharp.DataFeed.DataFeedTest

open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Contracts
open NBomber.FSharp

[<CLIMutable>]
type User = {
    Id: int
    Name: string
}

let run () =

    let data = [1; 2; 3; 4; 5] |> FeedData.fromSeq |> FeedData.shuffleData
    //let data = FeedData.fromJson<User>("./DataFeed/users-feed-data.json")
    //let data = FeedData.fromCsv<User>("./DataFeed/users-feed-data.csv")

    let feed = data |> Feed.createCircular "numbers"
    //let feed = data |> Feed.createConstant "numbers"
    //let feed = data |> Feed.createRandom "numbers"

    let step = Step.create("step", feed, fun context -> task {

        do! Task.Delay(seconds 1)

        context.Logger.Debug("Data from feed: {FeedItem}", context.FeedItem)
        return Response.Ok()
    })

    Scenario.create "data_feed_scenario" [step]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.run
    |> ignore
