module StructuralDictionaryTest

open Expecto
open Notedown.Core
open UnquoteAliases
open Swensen.Unquote.Assertions
open System.Collections.Generic

[<Tests>]
let structuralDictionaryTests =
    testList "Structural Dictionary" [
        testCase "should be equal when empty" <| fun () ->
            StructuralDictionary<int,int> () =! StructuralDictionary.empty
        testCase "should have the same values as the dictionary it's constructed from" <| fun () ->
            let likeness (dict:IDictionary<'key,'value>) : ('key*'value) list =
                dict |> Seq.map (fun kvp -> kvp.Key,kvp.Value) |> List.ofSeq

            let source = dict [1,2;3,4;5,6]
            let actual = StructuralDictionary(source)
            (likeness source) =! (likeness actual)

        testProperty "should be equal when instantiated from the same source" <| fun (dictionary: Dictionary<int,int>) ->
            StructuralDictionary (dictionary) =! StructuralDictionary (dictionary)
        testCase "should not be equal when a value is different" <| fun () ->
            sdict ["hi", 5] <>! sdict ["hi", 10]
        testCase "should not be equal when the keys differ" <| fun () ->
            sdict ["hi", 5] <>! sdict []

        // Should probably test other constructors if I release it as a library 
    ]