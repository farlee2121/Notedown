module MetadataModelTests

open System
open Expecto
open Notedown
open Notedown.Internal.BCLExtensions
open UnquoteAliases
open Swensen.Unquote.Assertions
open System.Collections.Generic
open FsCheck
open ExpectoExtensions


type DictLikeness<'key,'value> = ('key * 'value) list

module DictLikeness =
    let fromDict (dictionary: Dictionary<'key,'value>) = [for kvp in dictionary -> (kvp.Key, kvp.Value)]


type NoHeadingsString = NoHeadingsString of string
with
    member this.Get = match this with NoHeadingsString s -> s

type MetaSingle = private MetaSingle of MetadataValue
with
    member this.Get = match this with MetaSingle s -> s

type FsCheckExtensions () =
    let regexGen pattern = gen {
            let xeger = Fare.Xeger pattern
            return xeger.Generate()
        }
    static member NoHeadingsString () =
        gen {
            let xeger = Fare.Xeger "[^#]"
            return xeger.Generate()
        } |> Arb.fromGen

    static member MetaSingle() =
        Arb.generate<string>
        |> Gen.map MetadataValue.SingleValue
        |> Arb.fromGen

let testProperty' name test =
    testPropertyWithConfig { FsCheckConfig.defaultConfig with arbitrary = [typeof<FsCheckExtensions>] } name test


let titleToHeadingLevel str =
    let poundCount = str |> Seq.where (fun c -> c = '#') |> Seq.length
    match poundCount with
    | 0 -> SectionLevel.Root
    | n -> SectionLevel.Heading n

let sectionFromTitle title children =
    {
        Level = titleToHeadingLevel title
        ExclusiveText = title
        Meta = MetadataValue.default'
        Children = children
    }

let trimLast list =
    let lastIndex = (List.length list) - 1
    let lastSection : Section = List.last list
    list |> List.updateAt lastIndex { lastSection with ExclusiveText = lastSection.ExclusiveText.TrimEnd()}

type SelectError = Result<string option, MetadataValue.SetFailureReason>


[<Tests>]
let metadataModelTests = testList "Note Model" [
    testCase
        "should return an empty root-level section when given an empty document"
        <| fun () ->
            let document = ""
            let expected = {
                Level = SectionLevel.Root
                Meta = MetadataValue.default'
                ExclusiveText = document
                Children = []
            }

            let actual = NoteModel.parse document

            expected =! actual

    testList "Meta" [
        testProperty'
            "should return a empty meta but all contents when given a document with no frontmatter"
            <| fun (document: NoHeadingsString) ->
            document.Get |> (not << String.IsNullOrWhiteSpace)  ==> lazy (
                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = document.Get
                    Children = []
                }

                let actual = NoteModel.parse document.Get

                expected =! actual
                document.Get =! actual.FullText())

        testCase
            "should parse simple key-value meta"
            <| fun () ->
                let expectedKey = "rating";
                let expectedValue = 5;
                let document =
                    $"\
                    ---\n\
                      {expectedKey}: {expectedValue}\n\
                    ---\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = sdict [expectedKey, (expectedValue |> string |> SingleValue)] |> Complex
                    ExclusiveText = document
                    Children = []
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse array meta"
            <| fun () ->
                let document =
                    "\
                    ---\n\
                      author: [Spencer, David, Joe]\n\
                    ---\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = sdict ["author", (["Spencer"; "David"; "Joe"] |> List.map SingleValue |> MetadataValue.Vector)] |> Complex
                    ExclusiveText = document
                    Children = []
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse heterogeneous arrays of meta"
            <| fun () ->
                let document =
                    "\
                    ---\n\
                      author: [Spencer, [David], {hi: 5}]\n\
                    ---\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = sdict [
                        "author", Vector [
                            SingleValue "Spencer"
                            Vector [ SingleValue "David"]
                            Complex (sdict ["hi", SingleValue "5"])
                        ]
                    ] |> Complex
                    ExclusiveText = document
                    Children = []
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse nested maps of meta"
            <| fun () ->
                // beware the funky spacing required here
                // \n  \  is adding two spaces in front of the nested properties then ignoring space just for code alignment.
                // Not sure how to make this more intuitive since F# doesn't support relative formatting like C#'s triple quote.
                // F# triple quote is just a literal string that ignores escape sequences
                let document =
                    "\
                    ---\n\
                      author: Spencer\n\
                      config: \n  \
                        foo: 5\n  \
                        bar: {baz: 8}\n\
                    ---\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.fromPairs [
                        "author", SingleValue "Spencer"
                        "config", MetadataValue.fromPairs [
                            "foo", SingleValue "5"
                            "bar", MetadataValue.fromPairs [
                                "baz", SingleValue "8"
                            ]
                        ]
                    ]
                    ExclusiveText = document
                    Children = []
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse headings without a codeblock as sections with no meta"
            <| fun () ->
                // TODO: maybe remake this as a property. Probably need to register a custom Arb
                let document =
                    "\
                    # Title\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = [
                        {
                            Level = SectionLevel.Heading 1
                            ExclusiveText = document
                            Meta = MetadataValue.default'
                            Children = []
                        }
                    ]
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse yaml code blocks under headers as meta"
            <| fun () ->
                // TODO: maybe remake this as a property. Probably need to register a custom Arb
                let document =
                    "\
                    # Title \n\
                    ```yml\n\
                      rating: 5\n\
                    ```\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = [
                        {
                            Level = SectionLevel.Heading 1
                            ExclusiveText = document
                            Meta = MetadataValue.fromPairs [
                                "rating", SingleValue "5"
                            ]
                            Children = []
                        }
                    ]
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should not parse code blocks not marked as yaml or yml"
            <| fun () ->
                // TODO: maybe remake this as a property. Probably need to register a custom Arb
                let document =
                    "\
                    # Title \n\
                    ```cs\n\
                      rating: 5\n\
                    ```\
                    "

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = [
                        {
                            Level = SectionLevel.Heading 1
                            ExclusiveText = document
                            Meta = MetadataValue.default'
                            Children = []
                        }
                    ]
                }

                let actual = NoteModel.parse document

                expected =! actual

    ]

    testList "Children" [

        testCase
            "should parse increasing header levels as siblings of the root document"
            <| fun () ->
                let lines = [
                    "#### h4"
                    "### H3"
                    "## H2"
                    "# H1"
                ]
                let document = String.joinLines lines

                let sectionFromTitle' title = sectionFromTitle title []

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children =
                        lines |> List.map sectionFromTitle'
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse mixed header levels as siblings if they share a next larger a parent "
            <| fun () ->
                let lines = [
                    "# Parent"
                    "#### h4"
                    "### H3"
                    "## H2"
                ]
                let document = String.joinLines lines

                let sectionFromTitle' title = sectionFromTitle title []

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children =
                       [
                        sectionFromTitle "# Parent" (lines |> List.skip 1 |> List.map sectionFromTitle')
                       ]
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse smaller headers under larger headers as children"
            <| fun () ->
                let lines = [
                    "# H1"
                    "## H2"
                    "### H3"
                    "#### h4"
                ]
                let document = String.joinLines lines

                let sectionFromTitle' title children = [sectionFromTitle title children]

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children =
                         List.foldBack sectionFromTitle' lines []
                }

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should parse multiple separate hierarchies"
            <| fun () ->
                let lines = [
                    "# Title"
                    "## H2"
                    "## H2 Again"
                    "### H3"
                    "# Title 2"
                    "## Sub of 2"
                ]
                let document = String.joinLines lines

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = [
                        sectionFromTitle "# Title" [
                            sectionFromTitle "## H2" []
                            sectionFromTitle "## H2 Again" [
                                sectionFromTitle "### H3" []
                            ]
                        ]
                        sectionFromTitle "# Title 2" [
                            sectionFromTitle "## Sub of 2" []
                        ]
                    ]
                }

                let actual = NoteModel.parse document

                expected =! actual
    ]

    testList "ExclusiveContents" [
        testCase
            "should not overlap for sibling sections"
            <| fun () ->
                let sectionsText = [
                    String.joinLines [
                        "## H2"
                        "I am contents"
                        "> I am also contents"
                    ]
                    String.joinLines [
                        "## H2 2"
                        "- I am contents"
                        "- _I am also contents"
                    ]
                    String.joinLines [
                        "## H2 3"
                        "- I am contents"
                        "- _I am also contents"
                    ]
                ]
                let document = String.joinLines sectionsText

                let sectionFromText text = sectionFromTitle text []

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = sectionsText |> List.map sectionFromText
                }

                let actual = NoteModel.parse document

                expected =! actual
                document =! Section.fullText actual

        testCase
            "should not overlap between parent and child"
            <| fun () ->
                let sectionsText = [
                    String.joinLines [
                        "# H1"
                        "I am contents"
                        "> I am also contents"
                    ]
                    String.joinLines [
                        "## H2"
                        "- I am contents"
                        "- _I am also contents_"
                    ]
                    String.joinLines [
                        "### H3"
                        "- I am final content"
                    ]
                ]
                let document = String.joinLines sectionsText

                let sectionFromText text children = [sectionFromTitle text children]

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = ""
                    Children = List.foldBack sectionFromText sectionsText []
                }

                let actual = NoteModel.parse document

                expected =! actual
                document =! Section.fullText actual

        testCase
            "should not overlap between root and headings"
            <| fun () ->
                let sectionsText = [
                    String.joinLines [
                        "I am root contents"
                        "> So much root content"
                    ]
                    String.joinLines [
                        "## H2"
                        "- I am contents"
                        "- _I am also contents_"
                    ]
                    String.joinLines [
                        "### H3"
                        "- I am final content"
                    ]
                ]
                let document = String.joinLines sectionsText

                let sectionFromText text children = [sectionFromTitle text children]

                let expected = {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.default'
                    ExclusiveText = sectionsText.Head
                    Children = List.foldBack sectionFromText sectionsText.Tail []
                }

                let actual = NoteModel.parse document

                expected =! actual
                document =! Section.fullText actual

    ]

    testList "Meta Inheritance" [
        testCase
            "should apply no inheritance by default"
            <| fun () ->
                let sectionsText = [
                    String.joinLines [
                        "---"
                        "key: value"
                        "---"
                    ]
                    String.joinLines [
                        "# Heading"
                        "```yml"
                        "hi: 5"
                        "```"
                    ]
                    String.joinLines [
                        "## Child Heading"
                        "```yml"
                        "yes: sir"
                        "```"
                    ]
                ]
                let document = String.joinLines sectionsText


                let getExpected [root; s1; s2] =  {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.fromPairs ["key", SingleValue "value"]
                    ExclusiveText = root
                    Children = [{
                        Level = SectionLevel.Heading 1
                        Meta = MetadataValue.fromPairs ["hi", SingleValue "5"]
                        ExclusiveText = s1
                        Children = [{
                            Level = SectionLevel.Heading 2
                            Meta = MetadataValue.fromPairs ["yes", SingleValue "sir"]
                            ExclusiveText = s2
                            Children = []
                        }]
                    }]
                }
                let expected = getExpected sectionsText

                let actual = NoteModel.parse document

                expected =! actual

        testCase
            "should map this parent-child example"
            <| fun () ->
                let sectionsText = [
                    String.joinLines [
                        "---"
                        "key: value"
                        "---"
                    ]
                    String.joinLines [
                        "# Heading"
                    ]
                    String.joinLines [
                        "# Heading"
                        "```yml"
                        "hi: 5"
                        "```"
                    ]
                    String.joinLines [
                        "## Child Heading"
                        "```yml"
                        "yes: sir"
                        "hi: 10"
                        "```"
                    ]
                ]
                let document = String.joinLines sectionsText

                let getExpected [root; s1; s2; s3] =  {
                    Level = SectionLevel.Root
                    Meta = MetadataValue.fromPairs ["key", SingleValue "value"]
                    ExclusiveText = root
                    Children = [{
                        Level = SectionLevel.Heading 1
                        Meta = MetadataValue.fromPairs [
                            "key", SingleValue "value"
                        ]
                        ExclusiveText = s1
                        Children = []
                    };
                    {
                        Level = SectionLevel.Heading 1
                        Meta = MetadataValue.fromPairs [
                            "key", SingleValue "value"
                            "hi", SingleValue "5"
                        ]
                        ExclusiveText = s2
                        Children = [{
                            Level = SectionLevel.Heading 2
                            Meta = MetadataValue.fromPairs [
                                "key", SingleValue "value"
                                "hi", SingleValue "10"
                                "yes", SingleValue "sir"
                            ]
                            ExclusiveText = s3
                            Children = []
                        }]
                    }]
                }
                let expected = getExpected sectionsText

                let actual = NoteModel.parse document |> NoteModel.Inheritance.parentChild

                expected =! actual

    ]


    testList "MetadataValue" [

        testCase "default should not mutate" <| fun () ->
            let m = MetadataValue.default'
            match m with
            | Complex d -> d["hi"] <- SingleValue "hey"
            | _ -> ()
            Complex (sdict []) =! MetadataValue.default'
            

        testList "merge" [

            testProperty' "SingleValues are replaced by override"
            <| fun (target:MetaSingle, overrides:MetaSingle) ->
                let expected = overrides.Get
                let actual = MetadataValue.merge target.Get overrides.Get

                expected =! actual

            testProperty' "Vectors are replaced by override"
            <| fun (targetList:MetadataValue list, overList: MetadataValue list) ->
                let target = MetadataValue.Vector targetList
                let overrides = MetadataValue.Vector overList

                let expected = overrides
                let actual = MetadataValue.merge target overrides

                expected =! actual

            testCase "Vectors are replaced by override, no recursion for complex values"
            <| fun () ->
                let target = MetadataValue.Vector [ MetadataValue.fromPairs ["key", SingleValue "value"; "hi", SingleValue "5"] ]
                let overrides = MetadataValue.Vector [ MetadataValue.fromPairs ["other", SingleValue "key"; "hi", SingleValue "10"] ]

                let expected = overrides
                let actual = MetadataValue.merge target overrides

                expected =! actual

            theory "Mismatched value kinds take override value" [
                SingleValue "5", Vector []
                SingleValue "5", Complex (sdict [])
                Vector [], SingleValue "5"
                Vector [], Complex (sdict [])
                Complex (sdict []), SingleValue "5"
                Complex (sdict []), Vector []
            ]
            <| fun (target, overrides) ->
                let expected = overrides
                let actual = MetadataValue.merge target overrides

                expected =! actual

            testCase "New keys in override are added"
            <| fun () ->
                let targetPairs = ["hi", SingleValue "5"]
                let overridePairs = ["key", SingleValue "value"]

                let expected = MetadataValue.fromPairs (List.concat [targetPairs; overridePairs])
                let actual = MetadataValue.merge (MetadataValue.fromPairs targetPairs) (MetadataValue.fromPairs overridePairs)

                expected =! actual

            testCase "Matching keys are overridden"
            <| fun () ->
                let target = MetadataValue.fromPairs ["hi", SingleValue "5"]
                let overrides = MetadataValue.fromPairs ["hi", SingleValue "10"]

                let expected = overrides
                let actual = MetadataValue.merge target overrides

                expected =! actual

            testCase "Nested complex meta is merged recursively"
            <| fun () ->
                let target = MetadataValue.fromPairs [
                    "hi", MetadataValue.fromPairs [
                        "mid", SingleValue "Range"
                        "key", MetadataValue.fromPairs [
                            "inner", SingleValue "value"
                            "other", SingleValue "value"
                        ]
                    ]
                ]
                let overrides = MetadataValue.fromPairs [
                    "hi", MetadataValue.fromPairs [
                        "mid", SingleValue "day"
                        "key", MetadataValue.fromPairs [
                            "inner", SingleValue "peace"
                            "notin", SingleValue "target"
                        ]
                    ]
                ]

                let expected = MetadataValue.fromPairs [
                    "hi", MetadataValue.fromPairs [
                        "mid", SingleValue "day"
                        "key", MetadataValue.fromPairs [
                            "inner", SingleValue "peace"
                            "other", SingleValue "value"
                            "notin", SingleValue "target"
                        ]
                    ]
                ]
                let actual = MetadataValue.merge target overrides

                expected =! actual
        ]

        let genSelectorSegment _ = Guid.NewGuid ()
        let genSelector depth =
            List.init depth genSelectorSegment|> List.map string |> String.join "."
        testList "select/clobber" [
            

            testCase "it should return none if a value doesn't exist at the given selector" <| fun () ->
                let meta = MetadataValue.default'
                let depth = System.Random.Shared.Next(10)
                let selector = genSelector depth
                None =! MetadataValue.trySelect selector meta

            testCase "select should roundtrip with clobber at the root level" <| fun () ->

                let meta = MetadataValue.default'
                let expected = Guid.NewGuid().ToString()
                let selector = ""

                let updatedMeta = MetadataValue.clobber selector meta (SingleValue expected)
                let actual = MetadataValue.trySelectSingle selector updatedMeta
                Some expected =! actual

            testCase "select should roundtrip with clobber at any level" <| fun () ->

                let meta = MetadataValue.default'
                let expected = Guid.NewGuid().ToString()
                let selector = genSelector (System.Random.Shared.Next(10))

                let updatedMeta = MetadataValue.clobber selector meta (SingleValue expected)
                let actual = MetadataValue.trySelectSingle selector updatedMeta
                Some expected =! actual

            testCase "clobber should overwrite intermediate values to create the right path" <| fun () ->

                let meta = SingleValue "hi"
                let expected = Guid.NewGuid().ToString()
                let selector = genSelector (System.Random.Shared.Next(10))

                let updatedMeta = MetadataValue.clobber selector meta (SingleValue expected)
                let actual = MetadataValue.trySelectSingle selector updatedMeta
                Some expected =! actual

            testCase "should pass this demonstrative example" <| fun () ->
                let meta = (Complex << sdict) [
                    "level1", (Complex << sdict) [
                        "level2", (Complex << sdict) [
                            "level3", SingleValue "target"
                        ]
                    ]
                ]
                let selector = "level1.level2.level3"

                Some "target" =! MetadataValue.trySelectSingle selector meta

                let updated = MetadataValue.clobber selector meta (SingleValue "hit!")
                Some "hit!" =! MetadataValue.trySelectSingle selector updated


        ]

        testList "select/trySet" [

            testCase "trySet will not overwrite the root" <| fun () ->

                let meta = MetadataValue.default'
                let writeValue = Guid.NewGuid().ToString()
                let selector = ""

                let updatedMeta = MetadataValue.trySet selector meta (SingleValue writeValue)
                let expected = (Error << MetadataValue.SetFailureReason.WouldOverwrite) {Path = selector; Value = meta }
                let actual = updatedMeta |> Result.map (MetadataValue.trySelectSingle selector) 
                expected =! actual

            testCase "trySet will succeed for any non-existant path" <| fun () ->

                let meta = MetadataValue.default'
                let writeValue = Guid.NewGuid().ToString()
                let selector = genSelector (System.Random.Shared.Next(1,10))

                let expected = (Ok << Some) writeValue
                let updatedMeta = MetadataValue.trySet selector meta (SingleValue writeValue)
                let actual = updatedMeta |> Result.map (MetadataValue.trySelectSingle selector) 
                expected =! actual

            testCase "trySet will succeed for any non-destructive path" <| fun () ->

                let writeValue = Guid.NewGuid().ToString()
                let selector = genSelector (System.Random.Shared.Next(1,10))
                let pathEnsured = MetadataValue.clobber selector MetadataValue.default' (SingleValue writeValue)

                let expected = (Ok << Some) writeValue
                let updatedMeta = MetadataValue.trySet selector pathEnsured (SingleValue writeValue)
                let actual = updatedMeta |> Result.map (MetadataValue.trySelectSingle selector) 
                expected =! actual

            testCase "trySet will not overwrite SingleValues to create the right path" <| fun () ->

                let writeValue = Guid.NewGuid().ToString()
                let depth = System.Random.Shared.Next(1,10)
                let selector = genSelector depth

                let overwriteDepth = System.Random.Shared.Next(0,depth)
                let overwriteLocation = selector |> MetadataValue.Selector.segments |> Array.take overwriteDepth |> MetadataValue.Selector.join
                let overwriteValue = (SingleValue "hi")

                let overwriteEnsured = MetadataValue.clobber overwriteLocation MetadataValue.default' overwriteValue

                let expected = (Error << MetadataValue.SetFailureReason.WouldOverwrite) {Path = overwriteLocation; Value = overwriteValue }
                let actual = MetadataValue.trySet selector overwriteEnsured (SingleValue writeValue)
                expected =! actual

            testCase "should pass this demonstrative example" <| fun () ->
                let meta = (Complex << sdict) [
                    "level1", (Complex << sdict) [
                        "level2", (Complex << sdict) [
                            "level3", SingleValue "target"
                        ]
                    ]
                ]
                let selector = "level1.level2.level3"

                let updated = MetadataValue.trySet selector meta (SingleValue "hit!")
                (Ok << Some) "hit!" =! (updated |> Result.map (MetadataValue.trySelectSingle selector))


        ]
    ]
]