namespace IsItKrampus.NET.DataSet.Server

open System
open System.IO
open System.Threading
open System.Threading.Tasks
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
    let rawPath =
        "/home/gregor/source/repos/IsItKrampus.NET/data/raw"

    [<Literal>]
    let testImageName = "dog_test_image.jpeg"

    let testImage = Path.Combine(rawPath, testImageName)

    [<Literal>]
    let preparedPath =
        "/home/gregor/source/repos/IsItKrampus.NET/data/prepared"

    let createProcessingApi (logger: ILogger) =
        { getNextImageBase64 =
              fun () ->
                  async {
                      let! bytes =
                          File.ReadAllBytesAsync testImage
                          |> Async.AwaitTask

                      let toProcess =
                        { FileName = testImageName
                          Base64Content = Convert.ToBase64String bytes }

                      return toProcess
                  }
          applyProcessing =
                fun (processed: Processed) ->
                    async {
                        let targetPath = Path.Combine(preparedPath, $"{Guid.NewGuid()}.jpg")

                        logger.LogInformation $"Trying to process with following data: %A{processed}"
                        try
                            let! bytes =
                                File.ReadAllBytesAsync(Path.Combine(rawPath, processed.FileName))
                                |> Async.AwaitTask

                            use image = Image.Load bytes

                            let toRectangle (box: BoundingBox) =
                                Rectangle(int box.X, int box.Y, int box.Width, int box.Height)

                            let newImage =
                                image.Clone(fun i ->
                                    i.Crop(toRectangle processed.BoundingBox)
                                        .Resize(299, 299) |> ignore)

                            do! newImage.SaveAsJpegAsync targetPath |> Async.AwaitTask

                            return Ok
                        with
                        | ex ->
                            return Problem ex.Message
                    } }

    let createProcessingApiFromHttpContext (httpContext: HttpContext) =
        let logger = httpContext.GetService<ILogger<IProcessingApi>>()
        createProcessingApi logger

    let errorHandler (ex: Exception) (logger: ILogger) =
        logger.LogError(EventId(), ex, "An unhandled exception has occured.")
        clearResponse >=> ServerErrors.INTERNAL_ERROR ex.Message

    let remotingErrorHandler (ex: Exception) (routeInfo: RouteInfo<HttpContext>) =
        let logger = routeInfo.httpContext.GetService<ILogger<IProcessingApi>>()
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

        app.UseGiraffeErrorHandler(WebApp.errorHandler)
           .UseGiraffe WebApp.webApp
