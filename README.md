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

## Caveats

Only the first stream is downloaded

LL-HLS is not currently supported

Reports are lacking
