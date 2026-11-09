# Files, processes, the network

A program talks to the world outside it through the standard library: `std.fs` for files and
directories, `std.path` for the names of them, `std.process` for other programs, `std.net` and
`std.http` for the network. Everything that can fail out there throws — mostly an `IoError`, whose
`kind` says what went wrong and whose `path` says where; both live in `std.io`.

## Files

The shortest way in and out is a whole file at once: `writeText`, `readText`, and for bytes
`writeBytes` and `readBytes`. `std.path.join` puts a name together the way the system writes it:

```lyric
import std.fs { readText, writeText, tempDir, removeAll };
import std.io { println };
import std.path { join };

fn main(): void throws Error {
    let dir = try tempDir();
    let notes = join(dir, "notes.txt");
    try writeText(notes, "first\nsecond\nthird\n");
    let text = try readText(notes);
    println(f"{text.lines().count()} lines");
    try removeAll(dir);
}
```

```output
3 lines
```

`tempDir()` makes a fresh directory for the program; `removeAll` takes it away with what is in it.
A big file is better read line by line, without holding all of it: `lines(path)` gives an iterator
that reads as it goes. A file that is not there is an `IoError` of the kind `NotFound`:

```lyric
import std.fs { lines };
import std.io { println, IoError, IoErrorKind };

fn main(): void {
    try {
        for (line in lines("no/such/file.txt")) {
            println(line);
        }
    } catch (e: IoError) {
        if (e.kind == IoErrorKind.NotFound) {
            println(f"missing: {e.path ?? "?"}");
        }
    }
}
```

```output
missing: no/such/file.txt
```

For more control there is `File`, opened with `OpenOptions` — to append, to create a file that is
not there, to empty one that is, to read and write in one — and closed by `using`. A directory is
read with `readDir`, a whole tree with `walk`; `metadata` says how big a file is and when it changed.

## Processes

A `Command` describes a program to start: its name or path, its arguments, its working directory,
its environment, and where its standard streams go. `output()` runs it to its end and gives back
what it printed:

```lyric,no_run
import std.io { println };
import std.process { Command };

fn main(): void throws Error {
    let git = Command { program = "git", args = ["--version"] };
    let out = try git.output();
    if (out.status.success()) {
        println(string.fromUtf8Lossy(out.stdout).trim());
    } else {
        println(f"git ended with {out.status.code() ?? -1}");
    }
}
```

The name is looked up on `PATH`; a program that is not there throws `IoError` with the kind
`NotFound`. `output()` reads the child's output and errors concurrently and waits for its end; it is
a wait like any other, so the other tasks run meanwhile. `spawn()` instead returns the running
`Child`, with pipes to write its input and read its output piece by piece, and `wait()` for its end.

## The network

`std.net` has the sockets: `TcpListener` waits for connections, `TcpStream` is one, `UdpSocket` sends
datagrams, and `resolve` turns a host name into addresses. A stream is a `Reader` and a `Writer`, so
what reads and writes files reads and writes it too. Here a server and its client in one program,
on the loopback address and a port the system picks:

```lyric
import std.io { println };
import std.net { IpAddr, SocketAddr, TcpListener, TcpStream };
import std.task { spawn };

fn main(): void throws Error {
    var listener = try TcpListener.bind(SocketAddr { ip = IpAddr.V4(127, 0, 0, 1), port = 0 });
    let addr = try listener.localAddr();
    var server = spawn(() => {
        var conn = try listener.accept();
        let buf: uint8[] = [0u8] * 16;
        let n = try conn.read(buf[..]);
        try conn.write(buf[0..n]);
        try conn.close();
    });

    var client = try TcpStream.connect(addr);
    try client.write("ping".bytes()[..]);
    let reply: uint8[] = [0u8] * 16;
    let n = try client.read(reply[..]);
    println(string.fromUtf8Lossy(reply[0..n]));
    try client.close();
    try server.await();
    try listener.close();
}
```

```output
ping
```

The server is a task: while it waits in `accept()`, the client part of `main` runs. That is the whole
trick of a network program in Lyric — each connection in a task of its own, each waiting where it
must, and the waiting costs nothing.

Most programs do not speak TCP by hand. `std.http` has a `Client` that fetches a URL and a `Server`
that calls a function for each request. The next chapter builds on both: [a
server](project-server.md).
