namespace WorldBankBot

module Output =

    open System.Drawing
    open WorldBankBot.Core
    open FSharp.Data
    open FSharp.Charting

    let wb = WorldBankData.GetDataContext ()
    
    type WB = WorldBankData.ServiceTypes
    type Country = WB.Country
    type Indicator = Runtime.WorldBank.Indicator
    type Year = int
    type Value = float
    type Series = (Year*Value) list

    let grow f x =
        match x with
        | None -> None
        | Some(x) -> 
            match f x with
            | None -> None
            | Some(y) -> Some(x,y) 

    let strip (text:string) (target:string) = text.Replace(target,"")
    let excluded = [ " "; ","; "("; ")" ]

    let cleanup (text:string) = 
        excluded 
        |> List.fold (fun t x -> strip t x) text

    let lowerCase (text:string) = text.ToLowerInvariant ()

    let prepare = lowerCase >> cleanup

    // this is very likely terrible, perf-wise
    let like (requested:string) (target:string) = 
        (prepare target).Contains(prepare requested)

    let findCountry (name:string) =
        wb.Countries 
        |> Seq.tryFind (fun c -> c.Name |> like name)

    let findIndicator (name:string) (c:Country) =
        c.Indicators 
        |> Seq.tryFind (fun i -> i.Name |> like name)

    let getValues (year1,year2) (indicator:Indicator) =
        [ for year in year1 .. year2 -> year, indicator.[year]]

    let column (c:Country, i:Indicator, data:Series) =
        match data with
        | [] -> None
        | _ ->
            let title = sprintf "%s, %s" c.Name i.Name
            Chart.Column data
            |> Chart.WithTitle (Text=title,InsideArea=false)
            |> Some

    let lines (data:(Country*Indicator*Series) list) =
        let title = 
            data
            |> List.head
            |> fun (_,i,_) -> i.Name
        data
        |> List.map (fun (c,_,data) -> Chart.Line(data, Name=c.Name))
        |> Chart.Combine
        |> Chart.WithLegend(Title="Countries", InsideArea=false, Docking=ChartTypes.Docking.Right)
        |> Chart.WithTitle (title, InsideArea = true)
    
    type Result = { Description:string; Chart:ChartTypes.GenericChart option }

    let createChart (place:PLACE, values:MEASURE, timeframe:TIMEFRAME) =
        match (place, values, timeframe) with
        | COUNTRY(country), INDICATOR(indicator), OVER(year1,year2) ->             
            let data = 
                findCountry country
                |> grow (findIndicator indicator)
                |> Option.map (fun (c,i) -> c, i, getValues (year1,year2) i)
            match data with
            | Some(country,indicator,values) -> 
                let desc = sprintf "%s, %s (%i-%i)" country.Name indicator.Name year1 year2
                let chart = column (country,indicator,values)
                { Description = desc; Chart = chart }
            | None -> { Description = "Failed to retrieve data"; Chart = None }

        | COUNTRIES(countries), INDICATOR(indicator), OVER(year1,year2) -> 
            let data = 
                countries 
                |> List.map (fun country ->
                    findCountry country
                    |> grow (findIndicator indicator)
                    |> Option.map (fun (c,i) -> c, i, getValues (year1,year2) i))
                |> List.choose id
            match data with
            | [] -> { Description = "Failed to retrieve data"; Chart = None }
            | _ ->
                let chart = data |> lines |> Some
                let countryNames = 
                    data 
                    |> List.map (fun (c,_,_) -> c.Name)
                    |> String.concat ", "
                let description = sprintf "%s in %s (%i-%i)" indicator countryNames year1 year2
                { Description = description; Chart = chart }

        | COUNTRY(country), INDICATOR(indicator), IN(year) ->             
            let data = 
                findCountry country
                |> grow (findIndicator indicator)
            match data with
            | None -> { Description = "Failed to retrieve data"; Chart = None }
            | Some(country,indicator) ->
                let desc = sprintf "In %i, %s in %s was %.3f" year (indicator.Name) (country.Name) (indicator.[year])
                { Description = desc; Chart = None }

        | _ -> { Description = "Not supported yet"; Chart = None }       
         