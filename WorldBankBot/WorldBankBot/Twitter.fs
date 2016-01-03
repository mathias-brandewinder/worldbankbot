namespace WorldBankBot

module Twitter =

    open System
    open System.IO
    open System.Configuration
    open System.Threading
    open System.Threading.Tasks
    open System.Drawing
    open LinqToTwitter
    open FSharp.Charting
    open System.Net.Http
    
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

    let unixEpoch = DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)

    // 15 minutes is the Twitter reference, ex
    // 180 calls = 180/15 minutes window
    let callWindow = 15. 
    let safetyBuffer = 5. |> TimeSpan.FromSeconds

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
        printfn "Next call in %s" (wait |> string)

        let updatedSinceID =
            match mentions with
            | [] -> sinceID
            | hd::_ -> hd.StatusID |> Some
                                                                  
        mentions, updatedSinceID, wait

    let appData = 
        Environment.SpecialFolder.ApplicationData
        |> Environment.GetFolderPath
        
    let saveChart (chart:ChartTypes.GenericChart) =

        let filename = Guid.NewGuid().ToString() + ".png"
        let path = Path.Combine(appData,filename)        
        chart |> Chart.Save(path)
        path
        
    let uploadImage (path:string) =

        use img = Image.FromFile(path)
        use stream = new MemoryStream()
        img.Save(stream, Imaging.ImageFormat.Png)
          
        let upload = 
            Task.Run<Media>(fun _ -> 
                stream.ToArray ()
                |> context.UploadMediaAsync)
        let media = upload.Result
        
        img.Dispose ()
        File.Delete path

        media.MediaID

    let uploadChart (chart:ChartTypes.GenericChart) =

        chart
        |> saveChart
        |> uploadImage