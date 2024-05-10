# NLog.Targets.HTTP

[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NLog.Targets.HTTP)](https://www.nuget.org/packages/NLog.Targets.HTTP)

2024.05.10 - With the introduction of NLog 5.0 [WebServiceTarget](https://nlog-project.org/documentation/v5.0.0/html/T_NLog_Targets_WebServiceTarget.htm) 
this project will be heading towards becoming an archive. 

NLog.Targets.HTTP is an HTTP POST target for NLog. 
When combined with JSON formatter it can be used to send events to an 
instance of Splunk and other HTTP based collectors.

This target is inherently asynchronous, with performance on par or better than with the AsyncWrapper. [Remember to Flush](https://github.com/NLog/NLog/wiki/Tutorial#5-remember-to-flush) and to give it enough time to complete. 

Note that the `async="true"` attribute applied to `<targets >` will [discard by default](https://github.com/NLog/NLog/wiki/AsyncWrapper-target#async-attribute-will-discard-by-default).

## Getting started
Add the library as an extension to nlog:

```xml
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <extensions>
    <add assembly="NLog.Targets.Http" />
  </extensions>
  <targets>
    ...
```

### Available Configuration Parameters
Listed below are available configuration parameters with their default values (where applicable)
```xml
<target name='target name' 
        type='HTTP' 
        URL='protocol://server:port/path'
        Method='POST'
        Authorization='phrase token' 
        BatchSize='1'
        MaxQueueSize='2147483647'
        IgnoreSslErrors='true'
        FlushBeforeShutdown='true'
        ContentType='application/json'
        Accept='application/json'
        DefaultConnectionLimit='2'
        Expect100Continue='false'
        UseNagleAlgorithm='true'
        ConnectTimeout='30000' 
        InMemoryCompression='true'
        ProxyUrl=''
        ProxyUser=''
        ProxyPassword=''
    >
    <!-- optional additional HTTP Headers -->
    <header name='key' value='value'/>
    <!-- additional headers -->
    <!-- layout element -->
</target>
```

#### URL
The URL (`Layout` element) to send messages to (mandatory).

#### Method
HTTP method to use (GET,__POST__).

#### Authorization
The Authorization Header value to pass.

#### BatchSize
Number of messages to be sent together in one call.

#### BatchAsJsonArray
If set to `true`, messages will be packaged as JSON Array instead of being
separated with `Environment.NewLine` character. Default is `false`.

#### MaxQueueSize
Maximum number of messages awaiting to be send. Please note, that if this value is set too low, the logger might be blocking.

#### IgnoreSslErrors
Some SSL certificates might be invalid or not-trusted.

#### FlushBeforeShutdown
Force all messages to be delivered before shutting down. Note  that by design .Net apps are limited to about 2 seconds to shutdown.  
Make sure you leverage `LogManager.Flush(TimeSpan.FromHours(24))` in most extreme scenarios. 

#### ContentType
HTTP ContentType Header value. Default is `application/json`.

#### Accept
HTTP Accept Header value. Default is `application/json`.

#### DefaultConnectionLimit
How many connections might be used at the same time. Changes ServicePointManager.DefaultConnectionLimit, which might affect other parts of your system. 
Performance improvement was noticeable even with 16 connections, but your application might require more for other functionality. Use with caution.

#### Expect100Continue
See [this article](https://docs.microsoft.com/en-us/dotnet/api/system.net.servicepointmanager.expect100continue?view=netframework-4.8).

#### UseNagleAlgorithm 
The Nagle algorithm is used to buffer small packets of data and transmit them as a single packet. This process, referred to as "nagling," is widely used 
because it reduces the number of packets transmitted and lowers the overhead per packet. The Nagle algorithm is fully described in IETF RFC 896.

#### ConnectTimeout
How long should the client wait to connect (default is __30__ seconds).

#### InMemoryCompression
Reduces the amount of memory consumed at the expense of increased CPU usage. Significant performance improvement can be achieved by using default of `false`. 

#### ProxyUrl
Designates a proxy server to use. Must include protocol (http|https) and port. 
It is a `Layout` element so can be dynamic.

#### ProxyUser
If proxy authentication is needed, you can specify it with a domain prefix, i.e. DOMAIN\USER.

#### ProxyPassword
Password to use for proxy authentication.

#### Additional Headers
Additional HTTP Headers can be specified by adding multiple `<header name='..' value='..'/>` elements. 
Elements with a blank `name` or `value` will not be included.

## UnixTimeLayoutRenderer
The "Unix Time" renderer supports `universalTime` option (boolean), just like the date renderer does.
```xml
<attribute name='unixutc' layout='${unixtime:universalTime=true}' />
```

## Sample SPLUNK Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <targets>
    <target name='splunk' 
            type='HTTP' 
            URL='https://localhost:8088/services/collector/event'
            Authorization='Splunk d758f3fa-740f-4bb7-96be-3da4128708bc' 
            BatchSize='100'>
      <layout type='JsonLayout'>
        <attribute name='sourcetype' layout='_json' />
        <attribute name='host' layout='${machinename}' />
        <attribute name='event' encode='false'>
          <layout type='JsonLayout'>
            <attribute name='level' layout='${level:upperCase=true}' />
            <attribute name='source' layout='${logger}' />
            <attribute name='thread' layout='${threadid}' />
            <attribute name='message' layout='${message}' />
            <attribute name='utc' layout='${date:universalTime=true:format=yyyy-MM-dd HH\:mm\:ss.fff}' />
          </layout>
        </attribute>
      </layout>
    </target>
  </targets>
  <rules>
    <logger name="*" minlevel="Debug" writeTo="splunk" />
  </rules>
</nlog>
```


### Sample stats
On a Lenovo Xeon E3-1505M v6 powered laptop with 64GB of RAM and 3GB/s NVMe storage, 
this HTTP target was able to consistenlty accept about 125,000 messages per second, 
and 62,500 per second received and processed by local Dockerized Splunk Enterpise 8.2.4. 
Please note, that these stats depend heavily on the message size, batch size, and amount of bytes
that can be submitted in a single POST message. 

#### Running local Splunk

```shell
docker pull splunk/splunk:latest
docker run -d -p 8000:8000 -p 8088:8088 -e "SPLUNK_START_ARGS=--accept-license" -e "SPLUNK_PASSWORD=Pass@W0rd" --name splunk splunk/splunk:latest
```

After a few moments, depeneding on your systems capacity,
login to Splunk at http://localhost:8080/ with `admin` and `Pass@W0rd`. 
The HttpEventCollector (HEC) will listen on port 8088 once created from 
Settings - Data Inputs menu option.
