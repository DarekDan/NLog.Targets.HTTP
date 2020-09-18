# NLog.Targets.HTTP

[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NLog.Targets.HTTP)](https://www.nuget.org/packages/NLog.Targets.HTTP)

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
```

#### URL
The URL to send messages to (mandatory)

#### Method
HTTP method to use (GET,__POST__,PUT, etc.)

#### Authorization
The Authorization Header value to pass.

#### BatchSize
Number of messages to be sent together in one call separated by an empty new line

#### MaxQueueSize
Maximum number of messages awaiting to be send. Please note, that if this value is set too low, the logger might be blocking.

#### IgnoreSsslErrors
Some SSL certificates might be invalid or not-trusted.

#### FlushBeforeShutdown
Force all messages to be delivered before shutting down. Note  that by design .Net apps are limited to about 2 seconds to shutdown.  
Make sure you leverage `LogManager.Flush(TimeSpan.FromHours(24))` in most extreme scenarios. 

#### ContentType
HTTP ContentType Header value.

#### Accept
HTTP Accept Header value.

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
Reduces the amount of memory consumed at the expense of increased CPU usage. As much as 100% performance improvement can be achieved by setting this to `false`. 

#### ProxyUrl
Designates a proxy server to use. Must include protocol (http|https) and port

#### ProxyUser
If proxy authentication is needed, you can specify it with a domain prefix, i.e. DOMAIN\USER.

#### ProxyPassword
Password to use for proxy authentication.

### Sample SPLUNK Configuration

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
this HTTP target was able to clock almost 77,000 accepted messages per second, 
and over 31,000 per second received and processed by local Dockerized Splunk Enterpise 8.0.6.
 