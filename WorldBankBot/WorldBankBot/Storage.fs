namespace WorldBankBot

module Storage =

    open System.Configuration
    open Microsoft.WindowsAzure.Storage

    let appSettings = ConfigurationManager.AppSettings

    let apiKey = appSettings.["apiKey"]

    let storageConnection = appSettings.["azureStorage"]
    let containerName = appSettings.["lastmentioncontainer"]
    let blobName = appSettings.["lastmentionblob"]

    let container = 
        let account = CloudStorageAccount.Parse storageConnection
        let client = account.CreateCloudBlobClient ()
        client.GetContainerReference containerName

    let readLastMentionID () =
        let lastmention = container.GetBlockBlobReference blobName
        if lastmention.Exists ()
        then 
            lastmention.DownloadText () 
            |> System.Convert.ToUInt64
            |> Some
        else None

    let updateLastMentionID (ID:uint64) =
        let lastmention = container.GetBlockBlobReference blobName
        ID |> string |> lastmention.UploadText