# Homespun Azure Container Apps Infrastructure
#
# This Terraform configuration provisions the infrastructure required to run
# Homespun on Azure Container Apps with persistent storage.
#
# Prerequisites:
#   - Azure CLI installed and authenticated (az login)
#   - Terraform installed (v1.0+)
#   - Container image pushed to a registry (ACR or Docker Hub)
#
# Usage:
#   terraform init
#   terraform plan -var="github_token=ghp_xxx"
#   terraform apply -var="github_token=ghp_xxx"
#

terraform {
  required_version = ">= 1.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

# =============================================================================
# Variables
# =============================================================================

variable "resource_group_name" {
  description = "Name of the Azure resource group"
  type        = string
  default     = "rg-homespun"
}

variable "location" {
  description = "Azure region for resources"
  type        = string
  default     = "australiaeast"
}

variable "environment_name" {
  description = "Name of the Container Apps environment"
  type        = string
  default     = "homespun-env"
}

variable "app_name" {
  description = "Name of the Container App"
  type        = string
  default     = "homespun"
}

variable "container_image" {
  description = "Container image to deploy (e.g., myregistry.azurecr.io/homespun:latest)"
  type        = string
  default     = "homespun:latest"
}

variable "github_token" {
  description = "GitHub personal access token for PR operations"
  type        = string
  sensitive   = true
}

variable "tailscale_auth_key" {
  description = "Tailscale auth key (reusable, ephemeral disabled)"
  type        = string
  sensitive   = true
  default     = ""
}

variable "cpu" {
  description = "CPU cores for the container (0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0)"
  type        = number
  default     = 0.5
}

variable "memory" {
  description = "Memory for the container in Gi (must be compatible with CPU)"
  type        = string
  default     = "1Gi"
}

variable "min_replicas" {
  description = "Minimum number of replicas"
  type        = number
  default     = 1
}

variable "max_replicas" {
  description = "Maximum number of replicas"
  type        = number
  default     = 1
}

variable "use_acr" {
  description = "Whether to create and use Azure Container Registry"
  type        = bool
  default     = true
}

variable "acr_name" {
  description = "Name of the Azure Container Registry (must be globally unique)"
  type        = string
  default     = "acrhomespun"
}

# =============================================================================
# Resource Group
# =============================================================================

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location

  tags = {
    application = "homespun"
    environment = "production"
    managed_by  = "terraform"
  }
}

# =============================================================================
# Log Analytics Workspace
# =============================================================================

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${var.app_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = azurerm_resource_group.main.tags
}

# =============================================================================
# Azure Container Registry (Optional)
# =============================================================================

resource "azurerm_container_registry" "main" {
  count = var.use_acr ? 1 : 0

  name                = var.acr_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = true

  tags = azurerm_resource_group.main.tags
}

# =============================================================================
# Container Apps Environment
# =============================================================================

resource "azurerm_container_app_environment" "main" {
  name                       = var.environment_name
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  tags = azurerm_resource_group.main.tags
}

# =============================================================================
# Container Apps Environment Storage (for persistent data)
# =============================================================================

resource "azurerm_storage_account" "main" {
  name                     = "st${replace(var.app_name, "-", "")}data"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = azurerm_resource_group.main.tags
}

resource "azurerm_storage_share" "data" {
  name                 = "homespun-data"
  storage_account_name = azurerm_storage_account.main.name
  quota                = 5 # GB
}

resource "azurerm_container_app_environment_storage" "data" {
  name                         = "homespun-data"
  container_app_environment_id = azurerm_container_app_environment.main.id
  account_name                 = azurerm_storage_account.main.name
  share_name                   = azurerm_storage_share.data.name
  access_key                   = azurerm_storage_account.main.primary_access_key
  access_mode                  = "ReadWrite"
}

# =============================================================================
# Container App
# =============================================================================

resource "azurerm_container_app" "main" {
  name                         = var.app_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  # Use ACR credentials if ACR is enabled
  dynamic "registry" {
    for_each = var.use_acr ? [1] : []
    content {
      server               = azurerm_container_registry.main[0].login_server
      username             = azurerm_container_registry.main[0].admin_username
      password_secret_name = "acr-password"
    }
  }

  dynamic "secret" {
    for_each = var.use_acr ? [1] : []
    content {
      name  = "acr-password"
      value = azurerm_container_registry.main[0].admin_password
    }
  }

  secret {
    name  = "github-token"
    value = var.github_token
  }

  secret {
    name  = "tailscale-auth-key"
    value = var.tailscale_auth_key
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = "homespun"
      image  = var.use_acr ? "${azurerm_container_registry.main[0].login_server}/${var.container_image}" : var.container_image
      cpu    = var.cpu
      memory = var.memory

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }

      env {
        name  = "HOMESPUN_DATA_PATH"
        value = "/data/.homespun/homespun-data.json"
      }

      env {
        name        = "GITHUB_TOKEN"
        secret_name = "github-token"
      }

      env {
        name        = "TAILSCALE_AUTH_KEY"
        secret_name = "tailscale-auth-key"
      }

      env {
        name  = "TAILSCALE_HOSTNAME"
        value = "homespun-prod"
      }

      volume_mounts {
        name = "data"
        path = "/data"
      }

      liveness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080

        initial_delay           = 10
        interval_seconds        = 30
        timeout                 = 10
        failure_count_threshold = 3
      }

      readiness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080

        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 3
      }
    }

    volume {
      name         = "data"
      storage_name = azurerm_container_app_environment_storage.data.name
      storage_type = "AzureFile"
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "http"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  tags = azurerm_resource_group.main.tags
}

# =============================================================================
# Outputs
# =============================================================================

output "resource_group_name" {
  description = "Name of the resource group"
  value       = azurerm_resource_group.main.name
}

output "container_app_url" {
  description = "URL of the Container App"
  value       = "https://${azurerm_container_app.main.ingress[0].fqdn}"
}

output "container_app_name" {
  description = "Name of the Container App"
  value       = azurerm_container_app.main.name
}

output "acr_login_server" {
  description = "Login server for Azure Container Registry"
  value       = var.use_acr ? azurerm_container_registry.main[0].login_server : "N/A (ACR not enabled)"
}

output "acr_admin_username" {
  description = "Admin username for Azure Container Registry"
  value       = var.use_acr ? azurerm_container_registry.main[0].admin_username : "N/A (ACR not enabled)"
  sensitive   = true
}

output "log_analytics_workspace_id" {
  description = "ID of the Log Analytics workspace"
  value       = azurerm_log_analytics_workspace.main.id
}

output "storage_account_name" {
  description = "Name of the storage account for persistent data"
  value       = azurerm_storage_account.main.name
}

output "connection_info" {
  description = "Connection information"
  value       = <<-EOT
    
    ========================================
      Homespun Deployment Complete
    ========================================
    
    Container App URL: https://${azurerm_container_app.main.ingress[0].fqdn}
    Health Check:      https://${azurerm_container_app.main.ingress[0].fqdn}/health
    
    To view logs:
      az containerapp logs show -n ${azurerm_container_app.main.name} -g ${azurerm_resource_group.main.name} --follow
    
    To push a new image (if using ACR):
      az acr login --name ${var.use_acr ? azurerm_container_registry.main[0].name : "N/A"}
      docker tag homespun:latest ${var.use_acr ? azurerm_container_registry.main[0].login_server : "N/A"}/homespun:latest
      docker push ${var.use_acr ? azurerm_container_registry.main[0].login_server : "N/A"}/homespun:latest
      az containerapp update -n ${azurerm_container_app.main.name} -g ${azurerm_resource_group.main.name} --image ${var.use_acr ? azurerm_container_registry.main[0].login_server : "N/A"}/homespun:latest
    
    For Tailscale access, consider:
      - Running Tailscale as a sidecar container
      - Using Azure VPN Gateway with Tailscale on-prem
      - Using Tailscale Funnel for secure external access
    
  EOT
}
