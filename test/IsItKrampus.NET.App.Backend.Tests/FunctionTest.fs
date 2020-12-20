namespace IsItKrampus.NET.App.Backend.Tests

open System
open System.IO
open Xunit
open Amazon.Lambda.TestUtilities
open IsItKrampus.NET.App.Backend

module FunctionTest =
    let testKrampusBase64 =
        File.ReadAllLines("tstfile.txt")
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.head

    [<Fact>]
    let ``That inference works on obvious Krampus pic``() =
        // Invoke the lambda function and confirm the string was upper cased.
        let lambdaFunction = Function()
        let context = TestLambdaContext()
        let upperCase = lambdaFunction.FunctionHandler testKrampusBase64 context

        Assert.Equal("Krampus", upperCase)

    [<EntryPoint>]
    let main _ = 0
