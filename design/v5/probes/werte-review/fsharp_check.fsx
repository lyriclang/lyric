[<Struct>]
type S =
    val mutable x : int
    new(x) = { x = x }
    member this.Bump() = this.x <- this.x + 1
let s = S(1)
s.Bump()
printfn "F# let s after Bump: %d" s.x
let mutable m = S(1)
m.Bump()
printfn "F# let mutable m after Bump: %d" m.x
