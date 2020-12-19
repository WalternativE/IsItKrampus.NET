namespace IsItKrampus.NET.App.Backend

open System
open Amazon.Lambda.Core
open Microsoft.ML

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[<assembly: LambdaSerializer(typeof<Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer>)>]
()

type ModelInput =
    { Image: byte[]
      Label: string }

[<CLIMutable>]
type ModelOutput =
    { PredictedLabel: string }

type Function() =
    let mlContext = MLContext(1)
    let mutable predictionPipelineSchema : DataViewSchema = null
    let loadedModel = mlContext.Model.Load("./model.zip", &predictionPipelineSchema)
    let predictionEngine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(loadedModel)

    member __.FunctionHandler (input: string) (_: ILambdaContext) =
        match input with
        | null -> String.Empty
        | _ ->
            let prediction = predictionEngine.Predict({ Image = Convert.FromBase64String input; Label = null })
            prediction.PredictedLabel
