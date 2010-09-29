﻿module internal TickSpec.Parser

open TickSpec.LineParser

/// Build scenarios in specified lines
let buildScenarios lines =
    // Scan over lines
    lines
    |> Seq.scan (fun (scenario,lastStep,lastN,_) (n,line) ->        
        let step = 
            match parseLine (lastStep,line) with
            | Some newStep -> newStep
            | None -> 
                let e = expectingLine lastStep
                let m = sprintf "Syntax error on line %d %s\r\n%s" n line e
                StepException(m,n,scenario) |> raise                                                
        match step with
        | ScenarioStart(name) ->
            name, step, n, None
        | ExamplesStart          
        | GivenStep(_) | WhenStep(_) | ThenStep(_) ->                                          
            scenario, step, n, Some(scenario,n,line,step) 
        | TableRow(_) ->
            scenario, step, lastN, Some(scenario,lastN,line,step)                                           
    ) ("",ScenarioStart(""),0,None)
    // Handle tables
    |> Seq.choose (fun (_,_,_,step) -> step)
    |> Seq.groupBy (fun (_,n,_,_) -> n)
    |> Seq.map (fun (line,items) ->
        items |> Seq.fold (fun (row,table) (scenario,n,line,step) ->
            match step with
            | ScenarioStart _ -> 
                invalidOp("")
            | ExamplesStart
            | GivenStep _ | WhenStep _ | ThenStep _ ->
                (scenario,n,line,step),table
            | TableRow (_,columns) ->
                row,columns::table
        ) (("",0,"",ScenarioStart("")),[])
        |> (fun (line,table) -> 
            let table = List.rev table
            line,
                match table with
                | x::xs -> Some(Table(x,xs |> List.toArray))
                | [] -> None                 
        )
    )           
    // Group into scenarios
    |> Seq.map (fun ((scenario,n,line,step),table) ->
        scenario,n,line,step,table
    )
    |> Seq.groupBy (fun (scenario,_,_,_,_) -> scenario)                     
    // Handle examples
    |> Seq.map (fun (scenario,lines) -> 
        scenario,
            lines 
            |> Seq.toArray
            |> Array.partition (function 
                | _,_,_,ExamplesStart,_ -> true 
                | _ -> false                    
            )                                 
            |> (fun (examples,steps) ->
                steps, 
                    let tables =
                        examples      
                        |> Array.choose (fun (_,_,_,_,table) -> table)
                        |> Array.filter (fun table -> table.Rows.Length > 0)                                                                                                                        
                    if tables.Length > 0 then Some tables
                    else None                        
            )                                
    ) 
    |> Seq.map (fun (name,(steps,examples)) -> name,steps,examples)                        

/// Parse feature lines
let parse (featureLines:string[]) =       
    let startsWith s (line:string) = line.Trim().StartsWith(s)
    let lines =
        featureLines
        |> Seq.mapi (fun i line -> (i+1,line))          
    let n, feature =
        lines
        |> Seq.tryFind (snd >> startsWith "Feature")
        |> (function Some line -> line | None -> invalidOp(""))
    let scenarios =
        lines
        |> Seq.skip n
        |> Seq.skipUntil (snd >> startsWith "Scenario")
        |> Seq.filter (fun (_,line) -> line.Trim().Length > 0)          
        |> Seq.filter (fun (_,line) -> not(line.Trim().StartsWith("#")))
        |> buildScenarios
    feature, scenarios


