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

    let rec wait () =
        if (context.RateLimitRemaining > 5)
        then ignore ()
        else 
            let delay = 1000 * 120
            printfn "Waiting for %i ms" delay
            Thread.Sleep delay
            wait ()

    let pullMentions(sinceID:uint64 Option) =
        
        Thread.Sleep (1000 * 120)

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

        let updatedSinceID =
            match mentions with
            | [] -> sinceID
            | hd::_ -> hd.StatusID |> Some
                                                          
        mentions, updatedSinceID

    let appData = 
        Environment.SpecialFolder.ApplicationData
        |> Environment.GetFolderPath

    let uploadChart (chart:ChartTypes.GenericChart) =

        let filename = System.Guid.NewGuid().ToString() + ".png"
        let path = Path.Combine(appData,filename)

        chart |> Chart.Save(path)
        use img = Image.FromFile(path)
        use stream = new MemoryStream()
        img.Save(stream, Imaging.ImageFormat.Png)
          
        let upload = 
            Task.Run<Media>(fun _ -> 
                context.UploadMediaAsync(stream.ToArray()))
        let media = upload.Result
        
        img.Dispose ()
        File.Delete path

        media.MediaID
