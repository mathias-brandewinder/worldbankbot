namespace WorldBankBot

module Twitter =

    open System
    open System.IO
    open System.Configuration
    open System.Threading
    open System.Threading.Tasks
    open System.Drawing
    open System.Reflection
    open LinqToTwitter
    open FSharp.Charting
    open FSharp.Charting.ChartTypes
    open System.Net.Http
    open NLog

    let logger = LogManager.GetLogger "Twitter"

    // Twitter uses Unix time; let's convert to DateTime    
    let unixEpoch = DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
    let fromUnix (unixTime:int) =
        unixTime
        |> float
        |> TimeSpan.FromSeconds
        |> unixEpoch.Add
    
    let appSettings = ConfigurationManager.AppSettings

    let apiKey = appSettings.["apiKey"]
    let apiSecret = appSettings.["apiSecret"]
    let accessToken = appSettings.["accessToken"]
    let accessTokenSecret = appSettings.["accessTokenSecret"]

    let context = 
        let credentials = SingleUserInMemoryCredentialStore()
        credentials.ConsumerKey <- apiKey
        credentials.ConsumerSecret <- apiSecret
        credentials.AccessToken <- accessToken
        credentials.AccessTokenSecret <- accessTokenSecret
        let authorizer = SingleUserAuthorizer()
        authorizer.CredentialStore <- credentials
        new TwitterContext(authorizer)

    (*
    Handling Twitter rate limits: 
    context.RateLimit... gives us information
    on rate limits that apply to the previous
    call we made. If we have some rate left,
    just wait for the average time allowed
    between calls, otherwise, wait until the
    next rate limit reset time.
    *)

    // small utility to check how rates work
    let prettyTime (unixTime:int) = 
        (unixTime |> fromUnix).ToShortTimeString()

    let showCurrentLimits () =
        logger.Info (sprintf "Rate:  total:%i remaining:%i reset:%s" context.RateLimitCurrent context.RateLimitRemaining (context.RateLimitReset |> prettyTime))
        logger.Info (sprintf "Media: total:%i remaining:%i reset:%s" context.MediaRateLimitCurrent context.MediaRateLimitRemaining (context.MediaRateLimitReset |> prettyTime))
         
    // 15 minutes is the Twitter reference, ex
    // 180 calls = 180/15 minutes window
    let callWindow = 15. 
    let safetyBuffer = 5. |> TimeSpan.FromSeconds

    // how long until the rate limit is reset
    let resetDelay (context:TwitterContext) =
        let nextReset = 
            context.RateLimitReset
            |> fromUnix
        (nextReset + safetyBuffer) - DateTime.UtcNow
        
    let delayUntilNextCall (context:TwitterContext) =
        let delay =
            if context.RateLimitRemaining > 0
            then
                callWindow / (float context.RateLimitCurrent)
                |> TimeSpan.FromMinutes
            else
                let nextReset = 
                    context.RateLimitReset
                    |> float
                    |> TimeSpan.FromSeconds
                    |> unixEpoch.Add
                nextReset - DateTime.UtcNow
        delay.Add safetyBuffer

    let pullMentions(sinceID:uint64 Option) =
        
        let mentions = 
            match sinceID with
            | None ->
                query { 
                    for tweet in context.Status do 
                    where (tweet.Type = StatusType.Mentions)
                    select tweet }
            | Some(id) ->
                query { 
                    for tweet in context.Status do 
                    where (tweet.Type = StatusType.Mentions && tweet.SinceID = id)
                    where (tweet.StatusID <> id)
                    select tweet }
            |> Seq.toList

        let wait = delayUntilNextCall context
        logger.Info (sprintf "pullMentions: next call in %s" (wait |> string))

        let updatedSinceID =
            match mentions with
            | [] -> sinceID
            | hd::_ -> hd.StatusID |> Some
                                                                  
        mentions, updatedSinceID, wait

    let trimToTweet (msg:string) =
        if msg.Length > 140 
        then msg.Substring(0,134) + " [...]"
        else msg

    type Response = {
        RecipientName:string
        StatusID:uint64
        Message:string
        MediaID:uint64 option }

    let sendResponse (resp:Response) =
        
        let message = 
            sprintf "@%s %s" resp.RecipientName resp.Message
            |> trimToTweet

        match resp.MediaID with
        | None ->
            context.ReplyAsync(resp.StatusID, message) 
            |> ignore
        | Some(id) ->
            context.ReplyAsync(resp.StatusID, message, [id]) 
            |> ignore

    let setDescription () =
        let name = "World Bank Facts"
        let url = "https://github.com/mathias-brandewinder/worldbankbot"
        let location = "Somewhere in Azure"
        let time = DateTime.UtcNow.ToString("MMMM dd, yyyy, h:mm tt")
        let version = Assembly.GetEntryAssembly().GetName().Version.ToString()
        let description = sprintf "Proudly delivering artisanal World Bank facts with F# since %s [%s]" time version
        let skipStatus = false
        context.UpdateAccountProfileAsync(name, url, location, description, skipStatus)
        |> ignore

    let appData = 
        Environment.SpecialFolder.ApplicationData //.LocalApplicationData ///.ApplicationData
        |> Environment.GetFolderPath
        
    let saveChart (chart:ChartTypes.GenericChart) =

        logger.Info (sprintf "Saving chart")
        let filename = Guid.NewGuid().ToString() + ".png"
        let path = Path.Combine(appData,filename)        
        logger.Info (sprintf "Chart path: %s" path)
            
        use control = new ChartControl(chart)
        control.Size <- Size(1280,720)
        chart.CopyAsBitmap().Save(path, Imaging.ImageFormat.Png)
            
        logger.Info (sprintf "Chart saved at %s" path)
        path
        
    let uploadImage (path:string) =

        logger.Info (sprintf "Uploading image from %s" path)

        use img = Image.FromFile(path)
        use stream = new MemoryStream()
        img.Save(stream, Imaging.ImageFormat.Png)
          
        let upload = 
            Task.Run<Media>(fun _ -> 
                stream.ToArray ()
                |> context.UploadMediaAsync)
        
        let media = upload.Result

        logger.Info (sprintf "Media uploaded with ID %i" media.MediaID)

        printfn "Upload Media limit:"
        showCurrentLimits ()

        img.Dispose ()
        File.Delete path

        logger.Info (sprintf "Deleted file from %s" path)

        media.MediaID

    let uploadChart (chart:ChartTypes.GenericChart) =

        chart
        |> saveChart
        |> uploadImage

    type Agent<'T> = MailboxProcessor<'T>

    let responsesAgent = new Agent<Response>(fun inbox ->
        let rec loop () = async {
            let! response = inbox.Receive ()
            logger.Info (sprintf "Posting response: %s" response.Message)
            sendResponse response
            
            logger.Info "Send reply limit:"
            showCurrentLimits ()

            if (context.RateLimitRemaining = 0)
            then
                logger.Info "Send reply: waiting for limit reset"
                let wait = resetDelay context
                let ms = wait.TotalMilliseconds |> int
                do! Async.Sleep ms
            return! loop () }
        loop ())

    let mediaUploadAgent = new Agent<ChartTypes.GenericChart* AsyncReplyChannel<uint64>>(fun inbox ->
        let rec loop () = async {
            let! (chart, replyChannel) = inbox.Receive ()
            logger.Info "Uploading chart"
            let mediaID = uploadChart chart
            replyChannel.Reply mediaID
            logger.Info "Chart uploaded"

            logger.Info "Media upload limit:"
            showCurrentLimits ()

            if (context.RateLimitRemaining = 0)
            then
                logger.Info "Media upload: waiting for limit reset"
                let wait = resetDelay context
                let ms = wait.TotalMilliseconds |> int
                do! Async.Sleep ms
            return! loop () }
        loop ())

    mediaUploadAgent.Start ()
    responsesAgent.Start ()

