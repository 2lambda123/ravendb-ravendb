import genUtils = require("common/generalUtils");

class spatialCircleModel {

    static readonly colors = ["#F75E71", "#EDCD51", "#EE9D5F"];
   
    latitude: number;
    longitude: number;
    radius: number;
    
    constructor(circle: Raven.Client.Documents.Indexes.Spatial.Circle) {
        
        this.latitude = circle.Center.Latitude;
        this.longitude = circle.Center.Longitude;
        this.radius = genUtils.getMeters(circle.Radius, circle.Units);
    }
}

export = spatialCircleModel;
