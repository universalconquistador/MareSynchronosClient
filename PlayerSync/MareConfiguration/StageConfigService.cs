using MareSynchronos.MareConfiguration.Configurations;
using System;
using System.Collections.Generic;
using System.Text;

namespace MareSynchronos.MareConfiguration;

public class StageConfigService : ConfigurationServiceBase<StageConfig>
{
    public override string ConfigurationName => "stage.json";

    public StageConfigService(string configDir)
        : base(configDir)
    { }
}
