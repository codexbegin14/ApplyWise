# ApplyWise

A web-based platform that helps job seekers track job applications, manage resume versions, analyze resumes against job descriptions, identify missing skills, schedule interviews, and receive follow-up reminders.

## Core Problem

Job seekers often apply to many jobs using different resume versions and later forget where they applied, which resume they submitted, when to follow up, and why they are not getting responses.

## Main Features

- User authentication
- Resume version tracking
- Job application tracking
- Resume-to-job-description analysis
- Best resume suggestion
- Skill gap trends
- Interview scheduling
- Follow-up reminders
- Job scam detector
- Dashboard analytics
- PDF reports

## Tech Stack

- ASP.NET Core MVC
- C#
- Entity Framework Core
- SQL Server
- ASP.NET Core Identity
- Razor Views
- Bootstrap 5
- JavaScript
- Chart.js
- Hangfire
- QuestPDF
- UglyToad.PdfPig

## Project Status

- Level 1: Repository setup and structure
- Level 2: ASP.NET Core Identity and authenticated application shell
- Level 3: ApplyWise dashboard and responsive navigation
- Level 4: Private PDF resume upload and resume version management
- Level 5: Job application tracking
- Level 6: Local resume-to-job analysis and saved match history

## Level 5 Features

- Create, view, update, and delete job applications
- Track progress from saved opportunities through offer, rejection, or withdrawal
- Remember the resume version used or planned for every application
- Search by company or role and filter by status or source
- Sort applications by newest, oldest, deadline, or status
- Keep application details, job links, deadlines, descriptions, and private notes together

## Level 6 Features

- Extract readable text from uploaded PDF resumes with PdfPig
- Compare resume skills with the requirements detected in a saved job description
- Calculate a transparent keyword coverage score without paid AI APIs
- Show matched skills, missing skills, and honest improvement suggestions
- Save private analysis history and re-run comparisons after updating source data
