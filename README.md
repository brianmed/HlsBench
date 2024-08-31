# HlsBench

HLS Load Tester

Given a .m3u8 url will apply a synthetic load, using the given number of concurrent dowloads total number of HLS clients.  A summary of downloads is shown every 30 seconds.

## Examples

### 100 HLS Clients, 10 Concurrent Downloads

```bash
./hlsbench-osx-x64 --master-manifest-url='http://hostname.lan:8080/hls/sintel' --concurrent-downloads=10 --total-hls-clients=100
```

### 2000 HLS Clients, 100 Concurrent Downloads, Ramping Up 50 at a Time

```bash
./hlsbench-osx-x64 --master-manifest-url='http://hostname.lan:8080/hls/sintel' --concurrent-downloads=100 --total-hls-clients=2000 --rampup-hls-clients=50
```

## Installation

Single [file](https://github.com/brianmed/HlsBench/releases) deployment.

## Options

```
      --concurrent-downloads=VALUE
                             Count of Concurrent Downloads
      --count-cycle-hls-clients=VALUE
                             Number of HLS Clients to Stop and Start Every
                               TimeSpan seconds
      --seconds-cycle-hls-clients=VALUE
                             Every Number of Seconds Initiate Stop and Rampup
                               of HLS Clients
      --rampup-hls-clients=VALUE
                             Number of HLS Clients Provision each RampUp
      --rampup-seconds=VALUE Number of Seconds to Wait Between a RampUp
      --master-manifest-url=VALUE
                             HLS Master Manifest URL
      --total-hls-clients=VALUE
                             Total HLS Clients
  -h, --help                 Show this Message and Exit
```

## Builidng from Source

```
$ git clone https://github.com/brianmed/HlsBench.git
$ dotnet publish -c Release --self-contained
```

## Caveats

Only the first stream is downloaded

LL-HLS is not currently supported

Reports are lacking
