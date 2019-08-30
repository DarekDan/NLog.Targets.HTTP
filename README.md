# NLog.Targets.HTTP

NLog.Targets.HTTP is a HTTP POST target for NLog. 
Combined with JSON formatter it can be used to send events to an 
instance of Splunk and other HTTP based collectors.

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
Listed below are available configuration parameters with their default values
```xml
<target name='target name' 
        type='HTTP' 
        URL='protocol://server:port/path'
        Method='POST'
        Authorization='phrase token' 
        BatchSize='0'
        MaxQueueSize='2147483647'
        IgnoreSslErrors='true'
        FlushBeforeShutdown='true'
        ContentType='application/json'
        Accept='application/json'
        DefaultConnectionLimit='10'
        Expect100Continue='false'
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
Maximum number of messages awaiting to be send. Please note, that if this value is set, the logger will be blocking.

#### IgnoreSsslErrors
Some SSL certificates might be invalid or not-trusted.

#### FlushBeforeShutdown
Force all messages to be delivered before shutting down. 

#### ContentType
HTTP ContentType Header value.

#### Accept
HTTP Accept Header value.

#### DefaultConnectionLimit
How many connections might be used at the same time. Changes ServicePointManager.DefaultConnectionLimit if existing value is less than specified.

#### Expect100Continue
See [this article](https://docs.microsoft.com/en-us/dotnet/api/system.net.servicepointmanager.expect100continue?view=netframework-4.8).

#### ConnectTimeout
How long should the client wait to connect (default is __30__ seconds).

#### InMemoryCompression
Reduces the amount of memory consumed at the expense of increased CPU usage.

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
            type='HTTP' URL='server:port/services/collector'
            Authorization='Splunk auth-token' 
            BatchSize='100'>
      <layout type='JsonLayout'>
        <attribute name='sourcetype' layout='_json' />
        <attribute name='host' layout='myHostName' />
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
  </targets> <rules>
    <logger name="*" minlevel="Debug" writeTo="splunk" />
  </rules>
</nlog>
```

