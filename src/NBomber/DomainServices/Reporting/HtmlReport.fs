﻿module internal NBomber.DomainServices.Reporting.HtmlReport

open System

open NBomber.Domain.StatisticsTypes
open NBomber.Infra
open NBomber.Infra.ResourceManager
open NBomber.Infra.Dependency

type HtmlControl = { Html: string; Js: string; }

module IndicatorsChart =    

    let print (assets: Assets, viewId: string, label: string,
               latencyCount: LatencyCount, failCount: int) =

        let dataArray = 
            HtmlBuilder.toJsArray([latencyCount.Less800
                                   latencyCount.More800Less1200
                                   latencyCount.More1200
                                   failCount])

        let html = assets.IndicatorsChartHtml
                   |> String.replace("%viewId%", viewId)

        let js = assets.IndicatorsChartJs
                 |> String.replace("%dataArray%", dataArray)
                 |> String.replace("%viewId%", viewId)
                 |> String.replace("%label%", label)
                 
        { Html = html; Js = js }

module NumberReqChart =    

    let print (assets: Assets, viewId: string, okCount: int, failCount: int) =
        let dataArray = HtmlBuilder.toJsArray([okCount; failCount])
        
        let html = assets.NumReqChartHtml
                   |> String.replace("%viewId%", viewId)
        
        let js = assets.NumReqChartJs
                 |> String.replace("%dataArray%", dataArray)
                 |> String.replace("%viewId%", viewId)

        { Html = html; Js = js }

module StatisticsTable =    

    let print (assets: Assets, scnStats: ScenarioStats[]) =
        
        let printStepRow (step) =
            let stats = step.Percentiles.Value
            [step.StepName; step.ReqeustCount.ToString()
             step.OkCount.ToString(); step.FailCount.ToString()
             stats.RPS.ToString(); stats.Min.ToString(); stats.Mean.ToString(); stats.Max.ToString()
             stats.Percent50.ToString(); stats.Percent75.ToString(); stats.Percent95.ToString()
             stats.StdDev.ToString(); step.DataTransfer.MinKB.ToString(); step.DataTransfer.MeanKB.ToString()
             step.DataTransfer.MaxKB.ToString(); step.DataTransfer.AllMB.ToString()]
            |> HtmlBuilder.toTableRow

        let printScenarioRow (scnStats) =  

            let row = scnStats.StepsStats
                      |> Array.map(fun step -> printStepRow(step))
                      |> String.concat(String.Empty)                      
            
            let rowStr = row.Remove(0, 4)

            let tableTitle = sprintf "Statistics from scenario <b>%s</b> with concurrent copies: <b>%i</b>" scnStats.ScenarioName scnStats.ConcurrentCopies
            
            assets.StatisticsTableHtml
            |> String.replace("%table_title%", tableTitle)
            |> String.replace("%table_body%", "<tr>" + rowStr)

        scnStats
        |> Array.map(printScenarioRow)
        |> String.concat(String.Empty)        

module ScenarioView =    
        
    let createViewId (index: int) = String.Format("scenario-view-{0}", index)
    let createName (index: int) = String.Format("Scenario {0}", index)

    let print (assets: Assets, index: int, scnStats: ScenarioStats) = 

        let viewId = createViewId(index)
        let label = createName(index)

        let indicatorsChart = IndicatorsChart.print(assets, viewId, label, scnStats.LatencyCount, scnStats.FailCount)
        let numberReqChart = NumberReqChart.print(assets, viewId, scnStats.OkCount, scnStats.FailCount)

        let statisticsTableHtml = StatisticsTable.print(assets, [|scnStats|])
        
        let js = indicatorsChart.Js + numberReqChart.Js
        let html = assets.ScenarioViewHtml
                   |> String.replace("%viewId%", viewId)
                   |> String.replace("%statistics_table%", statisticsTableHtml)
                   |> String.replace("%indicators_chart%", indicatorsChart.Html)
                   |> String.replace("%num_req_chart%", numberReqChart.Html)                   
        
        { Html = html; Js = js }

module GlobalView =  

    let print (assets: Assets, stats: GlobalStats) =        
        
        let viewId = "global-view"
        let indicatorsChart = IndicatorsChart.print(assets, viewId, "All Scenarios", stats.LatencyCount, stats.FailCount)
        let numberReqChart = NumberReqChart.print(assets, viewId, stats.OkCount, stats.FailCount)
        
        let statisticsTableHtml = StatisticsTable.print(assets, stats.AllScenariosStats)

        let js = indicatorsChart.Js + numberReqChart.Js
        let html = assets.GlobalViewHtml
                   |> String.replace("%viewId%", viewId)
                   |> String.replace("%statistics_table%", statisticsTableHtml)
                   |> String.replace("%indicators_chart%", indicatorsChart.Html)
                   |> String.replace("%num_req_chart%", numberReqChart.Html)
        
        { Html = html; Js = js }

module EnvView =
    
    let print (assets: Assets, envInfo: MachineInfo) =            
        
        let title = "Cluster info"

        let row = [envInfo.MachineName                   
                   envInfo.OS.VersionString
                   envInfo.DotNetVersion
                   envInfo.Processor
                   envInfo.CoresCount.ToString()]
                  |> HtmlBuilder.toTableRow

        let envTable =
            assets.EnvTableHtml
            |> String.replace("%table_title%", title)
            |> String.replace("%env_table%", row)

        assets.EnvViewHtml
        |> String.replace("%viewId%", "env-view")
        |> String.replace("%env_table%", envTable)

module ContentView =

    let print (dep: Dependency, stats: GlobalStats) =        
        let envHtml = EnvView.print(dep.Assets, dep.MachineInfo)
        let globalView = GlobalView.print(dep.Assets, stats)
        
        let scnViews =
            if stats.AllScenariosStats.Length > 1 then
                let scnViews = stats.AllScenariosStats |> Array.mapi(fun i x -> ScenarioView.print(dep.Assets, i+1,x))
                let scnHtml = scnViews |> Array.map(fun x -> x.Html) |> String.concat(String.Empty)
                let scnJs = scnViews |> Array.map(fun x -> x.Js) |> String.concat(String.Empty)
                { Html = scnHtml; Js = scnJs }
            else
                { Html = ""; Js = "" }

        let html = envHtml + globalView.Html + scnViews.Html
        let js = globalView.Js + scnViews.Js
        { Html = html; Js = js }

module SideBar =

    let print (assets: Assets, stats: GlobalStats) =

        let printItem (assets, viewId, name, iconCss) =
            assets.SidebarItemHtml
            |> String.replace("%viewId%", viewId)
            |> String.replace("%name%", name)
            |> String.replace("%iconCss%", iconCss)

        let printScenarioItem (index) =
            let num = index + 1
            let viewId = ScenarioView.createViewId(num)
            let name = ScenarioView.createName(num)
            let css = "sub_icon fas fa-arrow-right"
            printItem(assets, viewId, name, css)

        let envItem = printItem(assets, "env-view", "Environment", "sub_icon fas fa-flask")
        let globalItem = printItem(assets, "global-view", "Global", "sub_icon fas fa-globe")
        
        let scnItems = 
            if stats.AllScenariosStats.Length > 1 then
                stats.AllScenariosStats
                |> Array.mapi(fun index _ -> printScenarioItem(index))
                |> String.concat(String.Empty)
            else
                String.Empty

        let sideBarItems = envItem + globalItem + scnItems
        assets.SidebarHtml.Replace("%sideBar_items%", sideBarItems)

let print (dep: Dependency, stats: GlobalStats) =
    let sideBar = SideBar.print(dep.Assets, stats)

    let contentView = ContentView.print(dep, stats)

    dep.Assets.IndexHtml
    |> String.replace("%sidebar%", sideBar)
    |> String.replace("%content_view%", contentView.Html)
    |> HtmlBuilder.toPrettyHtml
    |> String.replace("%js%", contentView.Js)