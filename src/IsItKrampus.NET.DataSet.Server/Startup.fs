namespace IsItKrampus.NET.DataSet.Server

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Giraffe
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open SixLabors.ImageSharp
open SixLabors.ImageSharp.Processing
open IsItKrampus.NET.DataSet.Shared

type Model = { Count: int }

module WebApp =
    [<Literal>]
    let dataRoot = "../../data"

    let rawPath = Path.Combine(dataRoot, "raw")

    [<Literal>]
    let testImageName = "dog_test_image.jpeg"

    let testImage = Path.Combine(rawPath, testImageName)

    [<Literal>]
    let preparedPath = "../../data/prepared"

    let downloadedImageFile = "image_downloads.tsv"

    let imagePrepFile = "image_prep.csv"

    let prepFileFullPath = Path.Combine(dataRoot, imagePrepFile)

    type DownloadedImage =
        { ImageUrl: string
          Id: Guid
          FileName: string }

    type ProcessedImage =
        { FileName: string
          Label: string
          X: int
          Y: int
          Height: int
          Width: int
          IsIncluded: bool
          Id: Guid }

    let createProcessingApi (logger: ILogger) =
        let rnd = Random(1)

        let getAllDownloadedImages () =
            async {
                let! content =
                    File.ReadAllLinesAsync(Path.Combine(dataRoot, downloadedImageFile))
                    |> Async.AwaitTask

                let lines =
                    content
                    |> Array.skip 1
                    |> Array.map
                        (fun line ->
                            let parts = line.Split('\t')

                            { ImageUrl = parts.[0]
                              Id = Guid.Parse parts.[1]
                              FileName = parts.[2] })

                return lines
            }

        let getAllProcessImages () =
            async {
                let! content =
                    File.ReadAllLinesAsync(prepFileFullPath)
                    |> Async.AwaitTask

                let lines =
                    content
                    |> Array.skip 1
                    |> Array.map
                        (fun line ->
                            let parts = line.Split(',')

                            { FileName = parts.[0]
                              Label = parts.[1]
                              X = Int32.Parse parts.[2]
                              Y = Int32.Parse parts.[3]
                              Height = Int32.Parse parts.[4]
                              Width = Int32.Parse parts.[5]
                              IsIncluded = Boolean.Parse parts.[6]
                              Id = Guid.Parse parts.[7] })

                return lines
            }

        let pickRandomUnprocessedImage () =
            async {
                let! downloadedImages = getAllDownloadedImages ()
                let! processedImages = getAllProcessImages ()

                let downloadedIds =
                    downloadedImages
                    |> Array.map (fun elem -> elem.Id)
                    |> Set.ofArray

                let processedIds =
                    processedImages
                    |> Array.map (fun elem -> elem.Id)
                    |> Set.ofArray

                let unprocessedIds =
                    Set.difference downloadedIds processedIds
                    |> Set.toArray

                let rndIdx =
                    rnd.Next(0, Array.length unprocessedIds - 1)

                let rndId = unprocessedIds.[rndIdx]

                return
                    (downloadedImages
                     |> Array.find (fun elem -> elem.Id = rndId), Array.length unprocessedIds)
            }

        { getNextImageBase64 =
              fun () ->
                  async {
                      let! (imageToProcess, stillToProcess) = pickRandomUnprocessedImage ()

                      let fileName =
                          Path.Combine(dataRoot, "raw", imageToProcess.FileName)

                      let! bytes = File.ReadAllBytesAsync fileName |> Async.AwaitTask

                      let toProcess =
                          { FileName = imageToProcess.FileName
                            Base64Content = Convert.ToBase64String bytes
                            StillToProcess = stillToProcess }

                      return toProcess
                  }
          applyProcessing =
              fun (processed: Processed) ->
                  async {
                      let id = processed.FileName.Split('.').[0]
                      let targetFileName = $"{id}.jpg"
                      let targetPath =
                          Path.Combine(preparedPath, targetFileName)

                      logger.LogInformation $"Trying to process with following data: %A{processed}"

                      try
                          let! bytes =
                              File.ReadAllBytesAsync(Path.Combine(rawPath, processed.FileName))
                              |> Async.AwaitTask

                          use image = Image.Load bytes

                          let toRectangle (box: BoundingBox) =
                              Rectangle(int box.X, int box.Y, int box.Width, int box.Height)

                          let newImage =
                              image.Clone
                                  (fun i ->
                                      i
                                          .Crop(toRectangle processed.BoundingBox)
                                          .Resize(299, 299)
                                      |> ignore)

                          do!
                              newImage.SaveAsJpegAsync targetPath
                              |> Async.AwaitTask

                          do!
                              let box = processed.BoundingBox
                              let line = $"{targetFileName},{processed.Label},{box.X},{box.Y},{box.Height},{box.Width},true,{id}"
                              File.AppendAllLinesAsync(prepFileFullPath, [ line ])
                              |> Async.AwaitTask

                          return Ok
                      with ex -> return Problem ex.Message
                  }
          excludeImage =
              fun (imageId: Guid) -> async {
                try
                    do!
                        let line = $"nah,Other,0,0,0,0,false,{imageId}"
                        File.AppendAllLinesAsync(prepFileFullPath, [ line ])
                        |> Async.AwaitTask
                    return Ok
                with ex -> return Problem ex.Message } }

    let createProcessingApiFromHttpContext (httpContext: HttpContext) =
        let logger =
            httpContext.GetService<ILogger<IProcessingApi>>()

        createProcessingApi logger

    let errorHandler (ex: Exception) (logger: ILogger) =
        logger.LogError(EventId(), ex, "An unhandled exception has occured.")

        clearResponse
        >=> ServerErrors.INTERNAL_ERROR ex.Message

    let remotingErrorHandler (ex: Exception) (routeInfo: RouteInfo<HttpContext>) =
        let logger =
            routeInfo.httpContext.GetService<ILogger<IProcessingApi>>()

        logger.LogError(EventId(), ex, "An unhandled exception has occured.")
        Ignore

    let webApp =
        Remoting.createApi ()
        |> Remoting.fromContext createProcessingApiFromHttpContext
        |> Remoting.withRouteBuilder (fun typeName methodName -> $"/api/%s{typeName}/%s{methodName}")
        |> Remoting.withErrorHandler remotingErrorHandler
        |> Remoting.buildHttpHandler

type Startup() =
    member _.ConfigureServices(services: IServiceCollection) = services.AddGiraffe() |> ignore

    member _.Configure(app: IApplicationBuilder, env: IWebHostEnvironment) =
        if env.IsDevelopment()
        then app.UseDeveloperExceptionPage() |> ignore

        app
            .UseGiraffeErrorHandler(WebApp.errorHandler).UseGiraffe WebApp.webApp
