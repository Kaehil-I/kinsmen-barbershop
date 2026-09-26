namespace Kinsmen.Api.Infrastructure;

// MongoDB's JSON-schema dialect uses bsonType for BSON dates and integers.
public static class Schemas
{
    public static string For(string name) => name switch
    {
        "services" => """
        {"bsonType":"object","required":["_id","name","priceCents","durationMinutes","active"],"properties":{
          "_id":{"bsonType":"string"},"name":{"bsonType":"string","minLength":1},
          "priceCents":{"bsonType":"int","minimum":0,"maximum":1000000},
          "durationMinutes":{"bsonType":"int","minimum":1,"maximum":480},"active":{"bsonType":"bool"}}}
        """,
        "barbers" => """
        {"bsonType":"object","required":["_id","name","userId","hours","active","revision"],"properties":{
          "_id":{"bsonType":"string"},"name":{"bsonType":"string"},"userId":{"bsonType":"string","minLength":1},
          "active":{"bsonType":"bool"},"revision":{"bsonType":"long","minimum":0},
          "hours":{"bsonType":"array","items":{"bsonType":"object","required":["weekday","startMinute","endMinute"],"properties":{
            "weekday":{"bsonType":"int","minimum":1,"maximum":7},"startMinute":{"bsonType":"int","minimum":0,"maximum":1439},
            "endMinute":{"bsonType":"int","minimum":1,"maximum":1440}}}}}}
        """,
        "bookings" => """
        {"bsonType":"object","required":["_id","customerId","barberId","startUtc","endUtc","services","totalCents","status","createdUtc","version"],"properties":{
          "_id":{"bsonType":"string"},"customerId":{"bsonType":"string","minLength":1},"barberId":{"bsonType":"string"},
          "startUtc":{"bsonType":"date"},"endUtc":{"bsonType":"date"},"createdUtc":{"bsonType":"date"},
          "totalCents":{"bsonType":"int","minimum":0},"version":{"bsonType":"long","minimum":1},
          "notes":{"bsonType":["string","null"],"maxLength":500},
          "creationFingerprint":{"bsonType":["string","null"],"maxLength":64},
          "status":{"enum":["Pending","Confirmed","Cancelled","Completed","NoShow"]},
          "services":{"bsonType":"array","minItems":1,"maxItems":10,"items":{"bsonType":"object",
            "required":["serviceId","name","priceCents","durationMinutes"],"properties":{
              "serviceId":{"bsonType":"string"},"name":{"bsonType":"string"},"priceCents":{"bsonType":"int","minimum":0},
              "durationMinutes":{"bsonType":"int","minimum":1,"maximum":480}}}}}}
        """,
        "timeBlocks" => """
        {"bsonType":"object","required":["_id","barberId","startUtc","endUtc","reason"],"properties":{
          "_id":{"bsonType":"string"},"barberId":{"bsonType":"string"},"startUtc":{"bsonType":"date"},"endUtc":{"bsonType":"date"},
          "reason":{"bsonType":"string","minLength":1,"maxLength":200}}}
        """,
        "reviews" => """
        {"bsonType":"object","required":["_id","bookingId","customerId","barberId","rating","createdUtc"],"properties":{
          "_id":{"bsonType":"string"},"bookingId":{"bsonType":"string","minLength":1},
          "customerId":{"bsonType":"string","minLength":1},"barberId":{"bsonType":"string","minLength":1},
          "rating":{"bsonType":"int","minimum":1,"maximum":5},
          "comment":{"bsonType":["string","null"],"maxLength":1000},
          "createdUtc":{"bsonType":"date"}}}
        """,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
