output "api_url" {
  value = aws_apigatewayv2_api.http.api_endpoint
}

output "cognito_user_pool_id" {
  value = aws_cognito_user_pool.main.id
}

output "cognito_user_pool_client_id" {
  value = aws_cognito_user_pool_client.web.id
}

output "cognito_domain" {
  value = "${aws_cognito_user_pool_domain.main.domain}.auth.${var.aws_region}.amazoncognito.com"
}

output "google_oauth_redirect_uri" {
  value = "https://${aws_cognito_user_pool_domain.main.domain}.auth.${var.aws_region}.amazoncognito.com/oauth2/idpresponse"
}

output "frontend_bucket_name" {
  value = aws_s3_bucket.frontend.id
}

output "cloudfront_distribution_id" {
  value = aws_cloudfront_distribution.frontend.id
}

output "cloudfront_domain_name" {
  value = aws_cloudfront_distribution.frontend.domain_name
}

output "lambda_function_names" {
  value = { for key, function in aws_lambda_function.this : key => function.function_name }
}

output "scheduler_schedule_group_name" {
  value = nonsensitive(aws_scheduler_schedule_group.matches.name)
}
