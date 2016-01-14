namespace WorldBankBot

open NLog

module Processor =

    open System
    open Core
    open LinqToTwitter
    open Twitter
    open System.Text.RegularExpressions
    open Parser
    open Output
    open Storage

    let logger = LogManager.GetLogger "Processor"
    
    let removeBotHandle text = 
        Regex.Replace(text, "@worldbankfacts", "", RegexOptions.IgnoreCase)

    let probablyQuery (text:string) =
        (text.Contains "COUNTR" 
        || text.Contains "INDICATOR" 
        || text.Contains "OVER"
        || text.Contains "IN")

    let (|Query|Mention|) text =
        if probablyQuery text 
        then Query
        else Mention
        
    let processArguments (args:PLACE*MEASURE*TIMEFRAME) (recipient,statusID) =
        async {
            logger.Info "Processing arguments"
            let result = createChart args
            let mediaID = 
                result.Chart 
                |> Option.map (fun chart -> 
                    Twitter.mediaUploadAgent.PostAndReply (fun channel -> chart, channel))

            { RecipientName = recipient
              StatusID = statusID
              Message = result.Description
              MediaID = mediaID }
            |> Twitter.responsesAgent.Post }

    let respondTo (status:Status) =
        
        let recipient = status.User.ScreenNameResponse
        let statusID = status.StatusID
        let text = status.Text

        sprintf "respondTo %s %i %s" recipient statusID text
        |> logger.Info

        match text with
        | Mention -> 
            { RecipientName = recipient
              StatusID = statusID
              Message = "thanks for the attention!"
              MediaID = None }
            |> Twitter.responsesAgent.Post
        | Query ->
            let arguments = 
                text
                |> removeBotHandle
                |> extractArguments
        
            match arguments with
            | Fail(msg) ->
                { RecipientName = recipient
                  StatusID = statusID
                  Message = "failed to parse your request: " + msg
                  MediaID = None }
                |> Twitter.responsesAgent.Post
            | OK(args) -> 
                processArguments args (recipient,statusID)
                |> Async.Start
                |> ignore
        
    let rec loop (sinceID:uint64 Option) = async {
        
        logger.Info "Checking for new mentions"

        let mentions, nextID, delay = pullMentions sinceID

        nextID 
        |> Option.iter (Storage.updateLastMentionID)
        
        mentions 
        |> List.iter respondTo

        do! Async.Sleep (delay.TotalMilliseconds |> int)

        return! loop (nextID) }
    
type Bot () =

    let logger = LogManager.GetLogger "Bot"

    member this.Start () =
        
        logger.Info "Service starting"

        Twitter.setDescription ()

        Storage.readLastMentionID ()
        |> Processor.loop 
        |> Async.Start

    member this.Stop () =
        logger.Info "Service stopped"