using System;
using System.Collections.Generic;

namespace USZ_ARTEMIS.DataExtraction
{
    class CouchDataExtractor
    {




        public static List<Nullable<double>> GetCouchHUValues(string couchModel)
        {
            // Return HU values to surface, interior and rails
            // null means automatic value, wich are -300, -1000 and 200 respectively
            switch (couchModel)
            {
                case "Exact_IGRT_Couch_Top_medium":
                    return new List<Nullable<double>> { -432, null, null };
                case "Exact_Couch_Top_with_Flat_panel":
                    return new List<Nullable<double>> { -900, -700, null };
                default:
                    return new List<Nullable<double>> { null, null, null };
            }
        }


    }
}
