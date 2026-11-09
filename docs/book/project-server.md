# Project: a server

The second project is a small web service: `jsonserver` keeps a list of items and answers HTTP
requests with JSON. `GET /items` gives the list, `GET /items/2` one item, `POST /items` adds one. The
same program is its own client: started without arguments, it serves on a port the system picks,
talks to itself, prints what came back and ends — which is how the test suite runs it. Started with
a port, it stays up for a browser or `curl` until Ctrl-C.

The finished program is [`examples/jsonserver`](../../examples/jsonserver/src/main.lyr). It uses
`std.http` on both sides, `std.json` between them, and a task to serve while `main` asks.

## The data

An item is a struct. Its conformance to `Codable` lets `std.json` write it and read it back, field
by field, by name — the compiler writes that code from the declaration:

```lyric
import std.codec { Codable };
import std.io { println };
import std.json { encode, decode };

struct Item :: [Codable, Equatable] {
    id: int,
    name: string,
    price: float,
}

fn main(): void throws Error {
    let bread = Item { id = 1, name = "Brot", price = 2.5 };
    let text = try encode(bread);
    println(text);
    let back = try decode<Item>(text);
    println(back == bread);
    let many = try decode<Item[]>("[{\"id\": 2, \"name\": \"Käse\", \"price\": 7.0}]");
    println(many[0].name);
}
```

```output
{"id":1,"name":"Brot","price":2.5}
true
Käse
```

A text that does not fit the type — a field missing, a string where a number belongs — throws a
`DecodeError` that says where — `[0].price` — and what was wrong. For a server that is the answer to a bad request, not a crash.

## The handler

A server is, at its heart, a function from a request to a response. `std.http` calls it for every
request — each connection is a task of its own —, and a handler that throws answers 500 with the
error's message:

```lyric,no_run
import std.http { Request, Response, Method };

fn handle(req: Request): Response throws Error {
    if (req.uri.path == "/hello" && req.method == Method.Get) {
        return Response.withText("hello\n");
    }
    return Response.withText("not here\n", 404);
}
```

Because it is a plain function, it can be called without any network at all — with a request made
by hand, its response read back:

```lyric
import std.codec { Codable };
import std.collections { List };
import std.http { Request, Response, Method };
import std.io { println };
import std.uri { Uri };

struct Item :: [Codable] {
    id: int,
    name: string,
}

struct Problem :: [Codable] {
    error: string,
}

let items: List<Item> = List<Item>.new();

fn handle(req: Request): Response throws Error {
    let path = req.uri.path;
    if (path == "/items" && req.method == Method.Get) {
        return try Response.withJson(items.toArray());
    }
    if (path.startsWith("/items/")) {
        let id = try int.parse(path.splitOnce("/items/")!.1);
        for (item in items) {
            if (item.id == id) {
                return try Response.withJson(item);
            }
        }
        return try Response.withJson(Problem { error = f"no item {id}" }, 404);
    }
    return try Response.withJson(Problem { error = f"no {path}" }, 404);
}

fn main(): void throws Error {
    items.push(Item { id = 1, name = "Brot" });
    for (path in ["/items", "/items/1", "/items/9"]) {
        let res = try handle(Request.get(Uri.parse("http://localhost" + path)));
        println(f"{res.status.code} {try res.text()}");
    }
}
```

```output
200 [{"id":1,"name":"Brot"}]
200 {"id":1,"name":"Brot"}
404 {"error":"no item 9"}
```

`items` is a module-level `let`: one list for the whole program, which every request sees. It is
safe here because the server's tasks all run on one thread and take turns only where they wait —
a handler that pushes an item does it in one piece. A server spread over several threads would put
the list behind a lock from `std.sync`.

The finished handler also takes a `POST`: `req.json<NewItem>()` reads the body as a type of its
own — a new item has no `id` yet —, and the response gets the status 201 and a `Location` header.

## Serving

`Server.bind` takes an address and opens the port; `serve` runs the handler for every request until
`shutdown()`. `serve` waits, so it runs in a task, and `main` is free to be the client:

```lyric
import std.http { Client, Request, Response, Server };
import std.io { println };
import std.net { IpAddr, SocketAddr };
import std.task { spawn };
import std.uri { Uri };

fn handle(req: Request): Response throws Error {
    return Response.withText(f"you asked for {req.uri.path}");
}

fn main(): void throws Error {
    var server = try Server.bind(SocketAddr { ip = IpAddr.V4(127, 0, 0, 1), port = 0 });
    let base = f"http://127.0.0.1:{(try server.localAddr()).port}";
    var serving = spawn(() => {
        try server.serve(handle);
    });

    var client = Client.new();
    let res = try client.get(Uri.parse(base + "/items"));
    println(f"{res.status.code} {try res.text()}");
    client.close();

    server.shutdown();
    try serving.await();
}
```

```output
200 you asked for /items
```

The port 0 asks the system for any free one, and `localAddr()` says which it gave — so two runs, or
two tests, never fight over a port. `shutdown()` stops taking connections and lets the ones in
flight finish; `await()` waits until `serve` has returned.

## Staying up

With a port on the command line, the finished program serves until it is told to stop.
`std.os.signals` turns the system's signals into a channel, so stopping is one more thing a task
waits for:

```lyric,no_run
import std.http { Request, Response, Server };
import std.io { eprintln };
import std.net { IpAddr, SocketAddr };
import std.os { signals, Signal };
import std.task { spawn };

fn handle(req: Request): Response throws Error {
    return Response.withText(f"hello from {req.uri.path}\n");
}

fn main(): void throws Error {
    var server = try Server.bind(SocketAddr { ip = IpAddr.V4(127, 0, 0, 1), port = 8080 });
    eprintln("serving on http://127.0.0.1:8080/");
    var serving = spawn(() => {
        try server.serve(handle);
    });
    let stop = signals(Signal.Interrupt, Signal.Terminate);
    let _ = try stop.recv();
    server.shutdown();
    try serving.await();
}
```

Ctrl-C sends `Interrupt`; a service manager stopping the program sends `Terminate`. Either way the
server finishes the requests it has and the program ends with 0, instead of being cut off mid-answer.

## What it used

Structs that are `Codable`; a module-level list; a handler as a plain function, tried without a
network; a server in a task and a client in `main`; a port the system picks; signals as a channel.
The next chapter is about making sure such a program keeps working: [testing and
tools](testing-and-tools.md).
