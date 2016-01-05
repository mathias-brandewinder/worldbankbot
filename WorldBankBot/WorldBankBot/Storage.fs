namespace WorldBankBot

module Storage =

    open System.Configuration
    open Microsoft.WindowsAzure.Storage

    let appSettings = ConfigurationManager.AppSettings

    let apiKey = appSettings.["apiKey"]

    let storageConnection = appSettings.["azureStorage"]
    let lastmentionContainerName = appSettings.["lastmentioncontainer"]
    let lastmentionBlob = appSettings.["lastmentionblob"]
    let chartsContainerName = appSettings.["chartscontainer"]

    let lastmentionContainer = 
        let account = CloudStorageAccount.Parse storageConnection
        let client = account.CreateCloudBlobClient ()
        client.GetContainerReference lastmentionContainerName

    let chartsContainer = 
        let account = CloudStorageAccount.Parse storageConnection
        let client = account.CreateCloudBlobClient ()
        client.GetContainerReference chartsContainerName

    let readLastMentionID () =
        let lastmention = lastmentionContainer.GetBlockBlobReference lastmentionBlob
        if lastmention.Exists ()
        then 
            lastmention.DownloadText () 
            |> System.Convert.ToUInt64
            |> Some
        else None

    let updateLastMentionID (ID:uint64) =
        let lastmention = lastmentionContainer.GetBlockBlobReference lastmentionBlob
        ID |> string |> lastmention.UploadText
