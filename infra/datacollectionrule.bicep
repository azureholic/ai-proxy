param location string = resourceGroup().location
param logAnalyticsWorkspaceResourceName string = 'law-proxy-dev'
param dataCollectionEndpointName string = 'ai-proxy-logs-dce'
param dataCollectionRuleName string = 'ai-proxy-logs-dcr'

var uniqueDestinationName = uniqueString('Custom-OpenAIChargeback_CL')

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview'  existing = {
  name: logAnalyticsWorkspaceResourceName
}

resource dataCollectionEndpoint 'Microsoft.Insights/dataCollectionEndpoints@2022-06-01' = {
  name: dataCollectionEndpointName
  location: location
  properties:{
    description: 'Data Collection Endpoint for Azure OpenAI Chargeback'
  }

}

resource dataCollectionRule 'Microsoft.Insights/dataCollectionRules@2022-06-01' = {
  location: location
  name: dataCollectionRuleName
  
  properties: {
    dataCollectionEndpointId: dataCollectionEndpoint.id
    streamDeclarations: {
      'Custom-OpenAIChargeback_CL': {
        columns: [
            {
                name: 'TimeGenerated'
                type: 'datetime'
            }
            {
                name: 'Consumer'
                type: 'string'
            }
            {
                name: 'Model'
                type: 'string'
            }
            {
                name: 'ObjectType'
                type: 'string'
            }
            {
                name: 'InputTokens'
                type: 'int'
            }
            {
                name: 'OutputTokens'
                type: 'int'
            }
            {
                name: 'TotalTokens'
                type: 'int'
            }
        ]
      } 
    }
    destinations: {
      logAnalytics: [
        {
        workspaceResourceId: logAnalytics.id
        name: uniqueDestinationName
        }
      ]
    }
    dataFlows: [
      {
        streams:[
          'Custom-OpenAIChargeback_CL'
        ]
        destinations: [
            uniqueDestinationName
        ]
        transformKql: 'source'
        outputStream: 'Custom-OpenAIChargeback_CL'
      }
    ]
  }
}
