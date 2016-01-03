namespace WorldBankBot

module Processor =

    open System
    open LinqToTwitter
    open Twitter
    open System.Text.RegularExpressions
    open Parser
    open Output
    open Storage

    let removeBotHandle text = 
        Regex.Replace(text, "@wbfacts", "", RegexOptions.IgnoreCase)

    let trimToTweet (msg:string) =
        if msg.Length > 140 
        then msg.Substring(0,134) + " [...]"
        else msg

    let sendResponse (author:string, statusID:uint64, message:string, mediaID:uint64 option) =
        
        let message = 
            sprintf "@%s %s" author message
            |> trimToTweet

        match mediaID with
        | None ->
            context.ReplyAsync(statusID, message) 
            |> ignore
        | Some(id) ->
            context.ReplyAsync(statusID, message, [id]) 
            |> ignore

    let respondTo (status:Status) =
        
        let author = status.User.ScreenNameResponse
        let statusID = status.StatusID

        let text = status.Text

        let arguments = 
            text
            |> removeBotHandle
            |> extractArguments
        
        match arguments with
        | None -> sendResponse (author, statusID, "failed to parse your request", None)
        | Some(args) -> 
            let result = createChart args
            let mediaID = result.Chart |> Option.map uploadChart
            sendResponse (author, statusID, result.Description, mediaID)
        
    let rec loop (sinceID:uint64 Option) = async {
        
        printfn "Checking for new mentions"

        let mentions, nextID = pullMentions sinceID

        nextID 
        |> Option.iter (Storage.updateLastMentionID)

        mentions 
        |> List.iter respondTo

        return! loop (nextID) }
    
type Bot () =

    member this.Start () =
        
        printfn "Service starting"
        Storage.readLastMentionID ()
        |> Processor.loop 
        |> Async.Start

    member this.Stop () =
        printfn "Service stopped"