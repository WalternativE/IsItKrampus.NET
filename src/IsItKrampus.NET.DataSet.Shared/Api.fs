namespace IsItKrampus.NET.DataSet.Shared

open System

type Label =
    | Santa
    | Krampus
    | Other

type ToProcess =
    { FileName: string
      Base64Content: string
      StillToProcess: int }

type BoundingBox =
    { X: float
      Y: float
      Width: float
      Height: float }

type Processed =
    { FileName: string
      BoundingBox: BoundingBox
      Label: Label }

type Result =
    | Ok
    | Problem of string

type IProcessingApi =
    { getNextImageBase64: unit -> Async<ToProcess>
      applyProcessing: Processed -> Async<Result>
      excludeImage: Guid -> Async<Result> }
